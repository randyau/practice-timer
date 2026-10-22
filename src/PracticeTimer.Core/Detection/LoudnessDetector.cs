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
    // A struck or plucked note rings on below the gate, more so in a noisy room where the gate sits higher, so a short
    // burst counts for at least this much loud time when judging whether someone is playing.
    private static readonly TimeSpan MinNoteDuty = TimeSpan.FromMilliseconds(150);
    // Steady loud sound with no rests (a fan, a hum) is room noise, not playing; see FeedSteadyNoise.
    private static readonly TimeSpan SteadyAfter = TimeSpan.FromSeconds(10);
    private const double SteadyRangeDb = 8;
    private const int SteadyWindowFrames = 320;
    private readonly Queue<double> _loudRun = new();
    private TimeSpan _loudFor;
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

    /// <summary>
    /// True while the user is teaching a metronome. The gate is made more sensitive and longer ticks are accepted,
    /// since a phone speaker is quiet and its click rings on. Nothing is being timed during a learn.
    /// </summary>
    public bool Learning { get; set; }

    private static readonly TimeSpan LearnShortMax = TimeSpan.FromMilliseconds(400);
    private const double LearnMarginDb = 8, LearnAbsoluteMinDb = -70;

    // The last two seconds of levels, so a learn can tell a tick from a steady background that sits just under the normal trigger.
    private const int LocalFrames = 80;
    private readonly double[] _local = new double[LocalFrames];
    private int _localCount, _localNext;

    /// <summary>A low percentile of recent levels: the background between ticks, however far above the floor it sits.</summary>
    private double LocalBackground(double db)
    {
        _local[_localNext] = db;
        _localNext = (_localNext + 1) % LocalFrames;
        _localCount = Math.Min(LocalFrames, _localCount + 1);
        Span<double> sorted = stackalloc double[LocalFrames];
        _local.AsSpan(0, _localCount).CopyTo(sorted);
        sorted = sorted[.._localCount];
        sorted.Sort();
        return sorted[(int)(_localCount * 0.3)];
    }

    public NoiseFloorEstimator Floor { get; } = new();
    public PeriodicityTracker Periodicity { get; } = new();

    public ActivitySample Process(AudioFrame frame)
    {
        var db = Dsp.Dbfs(frame.Samples);
        var background = LocalBackground(db);
        var gateFloor = Floor.Floor;
        _gate.MarginDb = _s.MarginDb;
        _gate.AbsoluteMinDb = _s.AbsoluteMinDb;
        _bursts.ShortMax = Learning ? LearnShortMax : _s.ShortBurstMax;
        if (Learning)
        {
            // More sensitive than normal, but measured from the background actually present, and never less strict
            // than the normal trigger would be: a steady hum just under it must not read as one endless sound.
            var learnFloor = Math.Max(gateFloor, background);
            var learnEnter = Math.Max(learnFloor + LearnMarginDb, LearnAbsoluteMinDb);
            if (learnEnter < _gate.EnterThreshold(gateFloor))
            {
                _gate.MarginDb = LearnMarginDb;
                _gate.AbsoluteMinDb = LearnAbsoluteMinDb;
                gateFloor = learnFloor;
            }
        }
        var loud = _gate.Process(db, gateFloor);
        var now = frame.End;
        // The floor is frozen while the detector is active (including its hangover), so rests inside a phrase
        // can't drag it down to the instrument's own decay.
        if (!loud && now > _holdUntil) Floor.Add(db);
        else if (loud) FeedSteadyNoise(db, frame.Duration);
        if (!loud) { _loudRun.Clear(); _loudFor = TimeSpan.Zero; }
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
            var periodic = Periodicity.Observe(closed.Onset, closed.Spectrum);
            metronome = matched || periodic;
            if (closed.GapBefore >= CleanGap) { _pendingClean = closed; _pendingMatched = matched; }
            if (!metronome)
            {
                AddDuty(now, closed.Duration < MinNoteDuty ? MinNoteDuty : closed.Duration);
                active = DutyFraction(now) >= _s.DutyThreshold;
            }
        }
        return Emit(now, db, loud, active, metronome);
    }

    /// <summary>
    /// The floor only learns from quiet frames, so a step up in room noise bigger than the margin would never be
    /// learned and would read as playing forever. After 10 s of unbroken loudness whose 10th to 60th percentile spread
    /// over the last 8 s is within 8 dB, the level is treated as noise and fed to the floor, which then rises past it.
    /// Real playing has rests and dynamics well beyond that.
    /// </summary>
    private void FeedSteadyNoise(double db, TimeSpan frameLength)
    {
        _loudFor += frameLength;
        _loudRun.Enqueue(db);
        while (_loudRun.Count > SteadyWindowFrames) _loudRun.Dequeue();
        if (_loudFor < SteadyAfter || _loudRun.Count < SteadyWindowFrames) return;
        // Percentiles, not min and max, so ticks or notes riding on the noise don't make it look like it is varying.
        Span<double> sorted = stackalloc double[SteadyWindowFrames];
        var n = 0;
        foreach (var v in _loudRun) sorted[n++] = v;
        sorted.Sort();
        if (sorted[SteadyWindowFrames * 6 / 10] - sorted[SteadyWindowFrames / 10] <= SteadyRangeDb) Floor.Add(db);
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
}
