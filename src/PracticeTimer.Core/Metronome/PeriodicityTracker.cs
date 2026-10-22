using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Metronome;

/// <summary>
/// Recognises a steady pulse in the onsets of short bursts. Follows tempo changes. When spectra are supplied the ticks
/// must also sound alike: a metronome repeats one sound, while a rhythm played on an instrument or by hand varies.
/// </summary>
public sealed class PeriodicityTracker
{
    private readonly List<TimeSpan> _recent = new();
    private TimeSpan _interval;
    private TimeSpan _lastTick;
    private bool _locked;
    private readonly List<double[]?> _recentSpectra = new();
    private double[]? _reference;

    public double MinIntervalSeconds { get; set; } = 0.24;
    public double MaxIntervalSeconds { get; set; } = 2.0;
    public double Tolerance { get; set; } = 0.12;
    public int MinTicks { get; set; } = 4;
    /// <summary>Spectral cosine needed between consecutive ticks to lock, and between a tick and the locked sound to follow it.</summary>
    public double LockSimilarity { get; set; } = 0.95;
    public double FollowSimilarity { get; set; } = 0.90;
    public TimeSpan HoldTime { get; set; } = TimeSpan.FromSeconds(3);

    public double? CurrentBpm => _locked ? 60.0 / _interval.TotalSeconds : null;

    /// <summary>Drops the lock once the pulse has been silent for the hold time, so the tempo readout clears.</summary>
    public void Expire(TimeSpan now)
    {
        if (_locked && now - _lastTick > HoldTime) _locked = false;
    }

    /// <summary>True when this onset is part of the metronome pulse.</summary>
    public bool Observe(TimeSpan onset, double[]? spectrum = null)
    {
        if (_locked && onset - _lastTick > HoldTime) _locked = false;
        if (_locked)
        {
            var d = (onset - _lastTick).TotalSeconds;
            var iv = _interval.TotalSeconds;
            var k = Math.Round(d / iv);
            var sounds = spectrum == null || _reference == null || BandSpectrum.Cosine(_reference, spectrum) >= FollowSimilarity;
            if (k >= 1 && sounds && Math.Abs(d - k * iv) <= Tolerance * iv * Math.Max(1, k * 0.5))
            {
                if (k == 1) _interval = TimeSpan.FromSeconds(d);
                _lastTick = onset;
                _reference = spectrum ?? _reference;
                Remember(onset, spectrum);
                return true;
            }
        }
        Remember(onset, spectrum);
        return TryLock(onset, spectrum);
    }

    private void Remember(TimeSpan onset, double[]? spectrum)
    {
        _recent.Add(onset);
        _recentSpectra.Add(spectrum);
        if (_recent.Count > MinTicks) { _recent.RemoveAt(0); _recentSpectra.RemoveAt(0); }
    }

    private bool TryLock(TimeSpan onset, double[]? spectrum)
    {
        if (_recent.Count < MinTicks) return false;
        var gaps = new List<double>();
        for (var i = 1; i < _recent.Count; i++) gaps.Add((_recent[i] - _recent[i - 1]).TotalSeconds);
        var mean = gaps.Average();
        if (mean < MinIntervalSeconds || mean > MaxIntervalSeconds) return false;
        if (gaps.Any(g => Math.Abs(g - mean) > Tolerance * mean)) return false;
        for (var i = 1; i < _recentSpectra.Count; i++)
            if (_recentSpectra[i - 1] is { } a && _recentSpectra[i] is { } b && BandSpectrum.Cosine(a, b) < LockSimilarity) return false;
        _reference = spectrum;
        _interval = TimeSpan.FromSeconds(mean);
        _lastTick = onset;
        _locked = true;
        return true;
    }
}
