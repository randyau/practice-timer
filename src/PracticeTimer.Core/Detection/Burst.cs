using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Detection;

/// <summary>A run of loud frames. <see cref="Duration"/> spans onset to the end of the last loud frame.</summary>
public sealed record Burst(TimeSpan Onset, TimeSpan Duration, double PeakDb, double[] Spectrum, TimeSpan GapBefore, bool Sustained)
{
    public TimeSpan End => Onset + Duration;
}

public sealed class BurstTracker
{
    private TimeSpan _shortMax;
    private readonly TimeSpan _closeGap;
    private bool _open;
    private TimeSpan _onset, _lastLoudEnd, _prevEnd = TimeSpan.FromMinutes(-10), _gapBefore, _quiet;
    private double _peak;
    private readonly List<float[]> _samples = new();
    private int _rate;
    private const int MaxFingerprintFrames = 8;

    public BurstTracker(TimeSpan? shortMax = null, TimeSpan? closeGap = null)
    {
        _shortMax = shortMax ?? TimeSpan.FromMilliseconds(150);
        _closeGap = closeGap ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>Longest run of loud frames that still counts as a short burst (a tick, a pluck) rather than a held sound.</summary>
    public TimeSpan ShortMax { get => _shortMax; set => _shortMax = value; }

    public TimeSpan OpenDuration => _open ? _lastLoudEnd - _onset : TimeSpan.Zero;
    public bool IsSustained => _open && OpenDuration > _shortMax;

    /// <summary>Returns the burst that closed on this frame, if any.</summary>
    public Burst? Update(AudioFrame frame, bool loud, double dbfs)
    {
        if (loud)
        {
            if (!_open)
            {
                _open = true;
                _onset = frame.Start;
                _gapBefore = frame.Start - _prevEnd;
                _peak = double.MinValue;
                _samples.Clear();
            }
            _lastLoudEnd = frame.End;
            _quiet = TimeSpan.Zero;
            _peak = Math.Max(_peak, dbfs);
            _rate = frame.SampleRate;
            if (_samples.Count < MaxFingerprintFrames) _samples.Add(frame.Samples);
            return null;
        }
        if (!_open) return null;
        _quiet += frame.Duration;
        if (_quiet < _closeGap) return null;
        _open = false;
        _prevEnd = _lastLoudEnd;
        var dur = _lastLoudEnd - _onset;
        return new Burst(_onset, dur, _peak, BandSpectrum.BurstFingerprint(Joined(), _rate), _gapBefore, dur > _shortMax);
    }

    private float[] Joined()
    {
        var all = new float[_samples.Sum(f => f.Length)];
        var at = 0;
        foreach (var f in _samples) { f.CopyTo(all, at); at += f.Length; }
        return all;
    }
}
