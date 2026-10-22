using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Metronome;

namespace PracticeTimer.Core.Detection;

public sealed class LoudnessDetector : IActivityDetector
{
    private readonly DetectorSettings _s;
    private readonly BandSpectrum _spectrum;
    private readonly LoudnessGate _gate = new();
    private readonly BurstTracker _bursts;
    private readonly Queue<(TimeSpan End, TimeSpan Loud)> _duty = new();
    private TimeSpan _holdUntil = TimeSpan.MinValue;

    public LoudnessDetector(int sampleRate, DetectorSettings? settings = null, int frameSamples = 0)
    {
        _s = settings ?? new DetectorSettings();
        _spectrum = new BandSpectrum(sampleRate, frameSamples > 0 ? frameSamples : (int)Math.Round(sampleRate * 0.025));
        _bursts = new BurstTracker(_s.ShortBurstMax);
    }

    public NoiseFloorEstimator Floor { get; } = new();
    public PeriodicityTracker Periodicity { get; } = new();

    public ActivitySample Process(AudioFrame frame)
    {
        _gate.MarginDb = _s.MarginDb;
        _gate.AbsoluteMinDb = _s.AbsoluteMinDb;
        var db = Dsp.Dbfs(frame.Samples);
        var loud = _gate.Process(db, Floor.Floor);
        if (!loud) Floor.Add(db);
        var now = frame.End;

        if (!_s.MetronomeFilterEnabled) return Emit(now, db, loud, loud, false);

        var closed = _bursts.Update(frame, loud, db, () => _spectrum.Compute(frame.Samples));
        var active = false;
        var metronome = false;

        if (_bursts.IsSustained)
        {
            active = true;
        }
        else if (closed is { Sustained: false })
        {
            metronome = Periodicity.Observe(closed.Onset);
            if (!metronome)
            {
                AddDuty(now, closed.Duration);
                active = DutyFraction(now) >= _s.DutyThreshold;
            }
        }
        return Emit(now, db, loud, active, metronome);
    }

    private ActivitySample Emit(TimeSpan now, double db, bool loud, bool active, bool metronome)
    {
        if (active) _holdUntil = now + _s.Hangover;
        var on = now <= _holdUntil;
        var tag = on ? FrameTag.Active : metronome ? FrameTag.Metronome : loud ? FrameTag.Loud : FrameTag.Quiet;
        return new ActivitySample(now, on, tag, db);
    }

    /// <summary>Duty counts only short unmatched bursts, so a long note never vouches for clicks after it.</summary>
    private void AddDuty(TimeSpan end, TimeSpan loud) => _duty.Enqueue((end, loud));

    private double DutyFraction(TimeSpan now)
    {
        while (_duty.Count > 0 && _duty.Peek().End < now - _s.DutyWindow) _duty.Dequeue();
        return _duty.Sum(d => d.Loud.TotalSeconds) / _s.DutyWindow.TotalSeconds;
    }

    public void Reset()
    {
        _bursts.Reset();
        Periodicity.Reset();
        _duty.Clear();
        _holdUntil = TimeSpan.MinValue;
    }
}
