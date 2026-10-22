using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Metronome;

public enum AddKind { Added, Merged, Full }

public readonly record struct AddResult(AddKind Kind, MetronomeProfile Profile, bool PromotedTentative = false);

/// <summary>A user edit to a saved metronome, applied as a unit by <see cref="FingerprintBank.ApplyEdits"/>.</summary>
public readonly record struct ProfileEdit(string Id, string Name, bool Enabled, bool Remove);

/// <summary>
/// The set of known metronome fingerprints. The audio thread matches and learns while the UI thread lists and edits,
/// so every member takes the same lock and <see cref="Profiles"/> hands out copies, never live objects.
/// </summary>
public sealed class FingerprintBank
{
    private readonly object _gate = new();
    private readonly List<MetronomeProfile> _profiles = new();
    private long _tick;

    public int MaxProfiles { get; set; } = 4;
    public double MatchThreshold { get; set; } = 0.92;
    public double MergeThreshold { get; set; } = 0.9;
    /// <summary>Level tolerance around a cluster's learned peak. Wide on purpose: spectral shape matters more than loudness.</summary>
    public double LevelBandDb { get; set; } = 18;
    public int SustainedMatchLimit { get; set; } = 3;

    /// <summary>
    /// Upper band edge of the fingerprints being produced right now. Fingerprints taken with a different band layout
    /// (a low-sample-rate microphone) are not comparable, so they neither match nor merge.
    /// </summary>
    public double BandHiHz { get; set; } = BandSpectrum.DefaultHiHz;

    /// <summary>A snapshot copy; changing it does not change the bank.</summary>
    public IReadOnlyList<MetronomeProfile> Profiles
    {
        get { lock (_gate) return _profiles.Select(p => p.Clone()).ToArray(); }
    }

    /// <summary>Counts for the status line, taken without copying profiles.</summary>
    public (int Saved, int Tentative, int OtherMic) Summary(string? currentMicId)
    {
        lock (_gate)
        {
            var saved = _profiles.Count(p => p.Locked);
            return (saved, _profiles.Count - saved,
                _profiles.Count(p => p.Locked && p.MicDeviceId != null && currentMicId != null && p.MicDeviceId != currentMicId));
        }
    }

    public void Load(IEnumerable<MetronomeProfile> profiles)
    {
        lock (_gate)
        {
            _profiles.Clear();
            _profiles.AddRange(profiles.Take(MaxProfiles));
        }
    }

    public MetronomeProfile? Match(double[] spectrum, double peakDb)
    {
        lock (_gate)
        {
            foreach (var p in _profiles)
            {
                if (!p.Enabled || !SameLayout(p) || !p.Clusters.Any(c => Matches(c, spectrum, peakDb))) continue;
                p.LastMatched = ++_tick;
                return p;
            }
            return null;
        }
    }

    private bool SameLayout(MetronomeProfile p) => Math.Abs(p.EffectiveBandHiHz - BandHiHz) < 1;

    private bool Matches(MetronomeCluster c, double[] spectrum, double peakDb) =>
        Math.Abs(peakDb - c.MeanPeakDb) <= LevelBandDb && BandSpectrum.Cosine(c.Spectrum, spectrum) >= MatchThreshold;

    /// <summary>
    /// Adds or merges a candidate. A tentative (auto) candidate never alters a locked profile: if it resembles one
    /// it is reported as merged and dropped, because only a guided learn may refine saved metronomes.
    /// </summary>
    public AddResult TryAdd(MetronomeProfile candidate)
    {
        lock (_gate)
        {
            if (candidate.BandHiHz <= 0) candidate.BandHiHz = BandHiHz;
            var existing = _profiles.FirstOrDefault(p => SameLayout(p)
                && p.Clusters.Any(a => candidate.Clusters.Any(b => BandSpectrum.Cosine(a.Spectrum, b.Spectrum) >= MergeThreshold)));
            if (existing != null)
            {
                if (!candidate.Locked && existing.Locked) return new AddResult(AddKind.Merged, existing.Clone());
                var promoted = candidate.Locked && !existing.Locked;
                Merge(existing, candidate);
                return new AddResult(AddKind.Merged, existing.Clone(), promoted);
            }
            if (_profiles.Count >= MaxProfiles)
            {
                var victim = _profiles.Where(p => !p.Locked).OrderBy(p => p.LastMatched).FirstOrDefault();
                if (victim == null) return new AddResult(AddKind.Full, candidate);
                _profiles.Remove(victim);
            }
            candidate.LastMatched = ++_tick;
            _profiles.Add(candidate);
            return new AddResult(AddKind.Added, candidate.Clone());
        }
    }

    private static void Merge(MetronomeProfile into, MetronomeProfile from)
    {
        foreach (var n in from.Clusters)
        {
            var c = into.Clusters.FirstOrDefault(x => BandSpectrum.Cosine(x.Spectrum, n.Spectrum) >= TickClusterer.Threshold);
            if (c == null) { if (into.Clusters.Count < 3) into.Clusters.Add(n); continue; }
            c.Spectrum = BandSpectrum.Merge(c.Spectrum, c.Count, n.Spectrum, n.Count);
            c.MeanPeakDb = (c.MeanPeakDb * c.Count + n.MeanPeakDb * n.Count) / (c.Count + n.Count);
            c.Count += n.Count;
        }
        if (from.Locked && !into.Locked) { into.Locked = true; into.Name = from.Name; }
        if (from.Locked) into.Enabled = true; // teaching it again means it should be in use
        if (from.MinBpm > 0) into.MinBpm = into.MinBpm > 0 ? Math.Min(into.MinBpm, from.MinBpm) : from.MinBpm;
        into.MaxBpm = Math.Max(into.MaxBpm, from.MaxBpm);
    }

    /// <summary>A tentative profile that keeps matching long sounds was probably learned from playing.</summary>
    public void NoteSustained(double[] spectrum)
    {
        lock (_gate)
        {
            foreach (var p in _profiles.Where(p => !p.Locked && p.Enabled).ToList())
                if (SameLayout(p) && p.Clusters.Any(c => BandSpectrum.Cosine(c.Spectrum, spectrum) >= MatchThreshold) && ++p.SustainedMatches >= SustainedMatchLimit)
                    _profiles.Remove(p);
        }
    }

    /// <summary>Stamps the mic a profile was learned on. Used by the app after a guided learn.</summary>
    public void SetMic(string id, string? deviceId, string? deviceName)
    {
        lock (_gate)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            p.MicDeviceId = deviceId;
            p.MicName = deviceName;
        }
    }

    /// <summary>Applies renames, enable flags and removals together. Profiles not named in <paramref name="edits"/> are left alone.</summary>
    public void ApplyEdits(IEnumerable<ProfileEdit> edits)
    {
        lock (_gate)
        {
            foreach (var e in edits)
            {
                if (e.Remove) { _profiles.RemoveAll(p => p.Id == e.Id); continue; }
                var p = _profiles.FirstOrDefault(x => x.Id == e.Id);
                if (p == null) continue;
                if (!string.IsNullOrWhiteSpace(e.Name)) p.Name = e.Name.Trim();
                p.Enabled = e.Enabled;
            }
        }
    }

    public bool Remove(string id)
    {
        lock (_gate) return _profiles.RemoveAll(p => p.Id == id) > 0;
    }

    public void ClearTentative()
    {
        lock (_gate) _profiles.RemoveAll(p => !p.Locked);
    }
}
