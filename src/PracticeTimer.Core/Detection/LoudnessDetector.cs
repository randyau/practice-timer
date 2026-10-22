using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Metronome;

namespace PracticeTimer.Core.Detection;

public sealed class LoudnessDetector : IActivityDetector
{
    private readonly DetectorSettings _s;
    private readonly LoudnessGate _gate = new();
    private readonly BurstTracker _bursts;
    private readonly Queue<(TimeSpan End, TimeSpan Loud)> _duty = new();
    private TimeSpan _holdUntil = TimeSpan.MinValue;

    private static readonly TimeSpan CleanGap = TimeSpan.FromMilliseconds(80);
    private Burst? _pendingClean;
    private bool _pendingMatched;

    public LoudnessDetector(DetectorSettings? settings = null, FingerprintBank? bank = null)
    {
        _s = settings ?? new DetectorSettings();
        _bursts = new BurstTracker(_s.ShortBurstMax);
        Bank = bank ?? new FingerprintBank();
    }

    public FingerprintBank Bank { get; }
    public double ThresholdDb => _gate.EnterThreshold(Floor.Floor);
    public bool SustainedOpen => _bursts.IsSustained;
    public event Action<CleanTick>? CleanTick;

    public NoiseFloorEstimator Floor { get; } = new();
    public PeriodicityTracker Periodicity { get; } = new();

    public ActivitySample Process(AudioFrame frame)
    {
        _gate.MarginDb = _s.MarginDb;
        _gate.AbsoluteMinDb = _s.AbsoluteMinDb;
        var db = Dsp.Dbfs(frame.Samples);
        var loud = _gate.Process(db, Floor.Floor);
        var now = frame.End;
        // The floor is frozen while the detector is active (including its hangover), so rests inside a phrase
        // can't drag it down to the instrument's own decay.
        if (!loud && now > _holdUntil) Floor.Add(db);
        Periodicity.Expire(now);

        if (!_s.MetronomeFilterEnabled) return Emit(now, db, loud, loud, false);

        ReleaseCleanTick(now, loud);
        var closed = _bursts.Update(frame, loud, db);
        var active = false;
        var metronome = false;

        if (_bursts.IsSustained)
        {
            active = true;
        }
        else if (closed is { Sustained: true })
        {
            Bank.NoteSustained(closed.Spectrum);
            _pendingClean = null;
        }
        else if (closed is { Sustained: false })
        {
            var matched = Bank.Match(closed.Spectrum, closed.PeakDb) != null;
            var periodic = Periodicity.Observe(closed.Onset);
            metronome = matched || periodic;
            if (closed.GapBefore >= CleanGap) { _pendingClean = closed; _pendingMatched = matched; }
            if (!metronome)
            {
                AddDuty(now, closed.Duration);
                active = DutyFraction(now) >= _s.DutyThreshold;
            }
        }
        return Emit(now, db, loud, active, metronome);
    }

    private void ReleaseCleanTick(TimeSpan now, bool loud)
    {
        if (_pendingClean == null) return;
        if (loud) { _pendingClean = null; return; }
        if (now - _pendingClean.End < CleanGap) return;
        var tick = new CleanTick(_pendingClean, _pendingMatched);
        _pendingClean = null;
        CleanTick?.Invoke(tick);
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
        _pendingClean = null;
        _holdUntil = TimeSpan.MinValue;
    }
}
