using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Detection;

/// <summary>A run of loud frames. <see cref="Duration"/> spans onset to the end of the last loud frame.</summary>
public sealed record Burst(TimeSpan Onset, TimeSpan Duration, double PeakDb, double[] Spectrum, TimeSpan GapBefore, bool Sustained)
{
    public TimeSpan End => Onset + Duration;
}

public sealed class BurstTracker
{
    private readonly TimeSpan _shortMax;
    private readonly TimeSpan _closeGap;
    private bool _open;
    private TimeSpan _onset, _lastLoudEnd, _prevEnd = TimeSpan.FromMinutes(-10), _gapBefore, _quiet;
    private double _peak;
    private double[]? _sum;

    public BurstTracker(TimeSpan? shortMax = null, TimeSpan? closeGap = null)
    {
        _shortMax = shortMax ?? TimeSpan.FromMilliseconds(150);
        _closeGap = closeGap ?? TimeSpan.FromMilliseconds(50);
    }

    public TimeSpan OpenDuration => _open ? _lastLoudEnd - _onset : TimeSpan.Zero;
    public bool IsSustained => _open && OpenDuration > _shortMax;

    /// <summary>Returns the burst that closed on this frame, if any.</summary>
    public Burst? Update(AudioFrame frame, bool loud, double dbfs, Func<double[]> spectrum)
    {
        if (loud)
        {
            if (!_open)
            {
                _open = true;
                _onset = frame.Start;
                _gapBefore = frame.Start - _prevEnd;
                _peak = double.MinValue;
                _sum = null;
            }
            _lastLoudEnd = frame.End;
            _quiet = TimeSpan.Zero;
            _peak = Math.Max(_peak, dbfs);
            var s = spectrum();
            _sum = _sum == null ? (double[])s.Clone() : _sum.Zip(s, (a, b) => a + b).ToArray();
            return null;
        }
        if (!_open) return null;
        _quiet += frame.Duration;
        if (_quiet < _closeGap) return null;
        _open = false;
        _prevEnd = _lastLoudEnd;
        var dur = _lastLoudEnd - _onset;
        return new Burst(_onset, dur, _peak, BandSpectrum.Normalize(_sum!), _gapBefore, dur > _shortMax);
    }

    public void Reset()
    {
        _open = false;
        _prevEnd = TimeSpan.FromMinutes(-10);
    }
}
