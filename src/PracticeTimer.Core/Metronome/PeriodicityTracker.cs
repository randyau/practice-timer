namespace PracticeTimer.Core.Metronome;

/// <summary>Recognises a steady pulse in the onsets of short bursts. Follows tempo changes.</summary>
public sealed class PeriodicityTracker
{
    private readonly List<TimeSpan> _recent = new();
    private TimeSpan _interval;
    private TimeSpan _lastTick;
    private bool _locked;

    public double MinIntervalSeconds { get; set; } = 0.24;
    public double MaxIntervalSeconds { get; set; } = 2.0;
    public double Tolerance { get; set; } = 0.12;
    public int MinTicks { get; set; } = 4;
    public TimeSpan HoldTime { get; set; } = TimeSpan.FromSeconds(3);

    public double? CurrentBpm => _locked ? 60.0 / _interval.TotalSeconds : null;

    /// <summary>True when this onset is part of the metronome pulse.</summary>
    public bool Observe(TimeSpan onset)
    {
        if (_locked && onset - _lastTick > HoldTime) _locked = false;
        if (_locked)
        {
            var d = (onset - _lastTick).TotalSeconds;
            var iv = _interval.TotalSeconds;
            var k = Math.Round(d / iv);
            if (k >= 1 && Math.Abs(d - k * iv) <= Tolerance * iv * Math.Max(1, k * 0.5))
            {
                if (k == 1) _interval = TimeSpan.FromSeconds(d);
                _lastTick = onset;
                Remember(onset);
                return true;
            }
        }
        Remember(onset);
        return TryLock(onset);
    }

    private void Remember(TimeSpan onset)
    {
        _recent.Add(onset);
        if (_recent.Count > MinTicks) _recent.RemoveAt(0);
    }

    private bool TryLock(TimeSpan onset)
    {
        if (_recent.Count < MinTicks) return false;
        var gaps = new List<double>();
        for (var i = 1; i < _recent.Count; i++) gaps.Add((_recent[i] - _recent[i - 1]).TotalSeconds);
        var mean = gaps.Average();
        if (mean < MinIntervalSeconds || mean > MaxIntervalSeconds) return false;
        if (gaps.Any(g => Math.Abs(g - mean) > Tolerance * mean)) return false;
        _interval = TimeSpan.FromSeconds(mean);
        _lastTick = onset;
        _locked = true;
        return true;
    }

    public void Reset()
    {
        _recent.Clear();
        _locked = false;
    }
}
