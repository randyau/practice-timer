using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Metronome;

public enum AddKind { Added, Merged, Full }

public readonly record struct AddResult(AddKind Kind, MetronomeProfile Profile, bool PromotedTentative = false);

public sealed class FingerprintBank
{
    private readonly List<MetronomeProfile> _profiles = new();
    private long _tick;

    public int MaxProfiles { get; set; } = 4;
    public double MatchThreshold { get; set; } = 0.92;
    public double MergeThreshold { get; set; } = 0.9;
    public double LevelBandDb { get; set; } = 12;
    public int SustainedMatchLimit { get; set; } = 3;

    public IReadOnlyList<MetronomeProfile> Profiles => _profiles;

    public void Load(IEnumerable<MetronomeProfile> profiles)
    {
        _profiles.Clear();
        _profiles.AddRange(profiles.Take(MaxProfiles));
    }

    public MetronomeProfile? Match(double[] spectrum, double peakDb)
    {
        foreach (var p in _profiles)
        {
            if (!p.Enabled || !p.Clusters.Any(c => Matches(c, spectrum, peakDb))) continue;
            p.LastMatched = ++_tick;
            return p;
        }
        return null;
    }

    private bool Matches(MetronomeCluster c, double[] spectrum, double peakDb) =>
        Math.Abs(peakDb - c.MeanPeakDb) <= LevelBandDb && BandSpectrum.Cosine(c.Spectrum, spectrum) >= MatchThreshold;

    public AddResult TryAdd(MetronomeProfile candidate)
    {
        var existing = _profiles.FirstOrDefault(p => p.Clusters.Any(a => candidate.Clusters.Any(b => BandSpectrum.Cosine(a.Spectrum, b.Spectrum) >= MergeThreshold)));
        if (existing != null)
        {
            var promoted = candidate.Locked && !existing.Locked;
            Merge(existing, candidate);
            return new AddResult(AddKind.Merged, existing, promoted);
        }
        if (_profiles.Count >= MaxProfiles)
        {
            var victim = _profiles.Where(p => !p.Locked).OrderBy(p => p.LastMatched).FirstOrDefault();
            if (victim == null) return new AddResult(AddKind.Full, candidate);
            _profiles.Remove(victim);
        }
        candidate.LastMatched = ++_tick;
        _profiles.Add(candidate);
        return new AddResult(AddKind.Added, candidate);
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
        if (from.MinBpm > 0) into.MinBpm = into.MinBpm > 0 ? Math.Min(into.MinBpm, from.MinBpm) : from.MinBpm;
        into.MaxBpm = Math.Max(into.MaxBpm, from.MaxBpm);
    }

    /// <summary>A tentative profile that keeps matching long sounds was probably learned from playing.</summary>
    public void NoteSustained(double[] spectrum)
    {
        foreach (var p in _profiles.Where(p => !p.Locked && p.Enabled).ToList())
            if (p.Clusters.Any(c => BandSpectrum.Cosine(c.Spectrum, spectrum) >= MatchThreshold) && ++p.SustainedMatches >= SustainedMatchLimit)
                _profiles.Remove(p);
    }

    public bool Remove(string id) => _profiles.RemoveAll(p => p.Id == id) > 0;
    public void ClearTentative() => _profiles.RemoveAll(p => !p.Locked);
    public void Clear() => _profiles.Clear();
}
