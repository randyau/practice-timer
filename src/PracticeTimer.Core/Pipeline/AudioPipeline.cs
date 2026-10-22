using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Timing;

namespace PracticeTimer.Core.Pipeline;

/// <summary>What the UI needs to draw the level meter. Immutable, published by the audio thread.</summary>
public sealed record MeterSnapshot(
    double Dbfs, double FloorDb, double ThresholdDb, FrameTag Tag, double? Bpm,
    bool Calibrating)
{
    public static readonly MeterSnapshot Empty = new(Dsp.MinDb, NoiseFloorEstimator.DefaultFloorDb, -55, FrameTag.Quiet, null, false);
}

/// <summary>A calibrated noise floor and its bias, kept so a reopened microphone doesn't lose it.</summary>
public readonly record struct NoiseCalibration(double FloorDb, double Bias);

/// <summary>Mono samples in; clock activity, learning and meter data out. Safe to call from the audio thread.</summary>
public sealed class AudioPipeline
{
    private static readonly TimeSpan FrameLength = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan CalibrationLength = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly FrameSlicer _slicer;
    private readonly LoudnessDetector _detector;
    private readonly AutoLearner _auto;
    private readonly PracticeClock _clock;
    private readonly FingerprintBank _bank;
    private TimeSpan _now;
    private GuidedLearnSession? _learn;
    private bool _learnReported;
    private List<double>? _calibration;
    private volatile MeterSnapshot _snapshot = MeterSnapshot.Empty;
    private double _peakSince = Dsp.MinDb;
    private FrameTag _tagSince = FrameTag.Quiet;
    private bool _framesSince;
    private ActivitySample _last;
    private MeterSnapshot _lastTaken = MeterSnapshot.Empty;

    /// <summary><paramref name="noise"/> carries an earlier calibration over when the same microphone is reopened.</summary>
    public AudioPipeline(int sampleRate, PracticeClock clock, DetectorSettings settings, FingerprintBank bank, NoiseCalibration? noise = null)
    {
        _clock = clock;
        _bank = bank;
        _bank.BandHiHz = BandSpectrum.EffectiveHiHz(sampleRate);
        _slicer = new FrameSlicer(sampleRate, FrameLength);
        _detector = new LoudnessDetector(settings, bank);
        _auto = new AutoLearner(bank);
        if (noise is { } n) _detector.Floor.Restore(n.FloorDb, n.Bias);
        _detector.CleanTick += t =>
        {
            if (_learn is { IsDone: false }) _learn.OnCleanTick(t);
            else _auto.OnCleanTick(t);
        };
    }

    /// <summary>The latest frame's view. Fine for tests; the UI should use <see cref="TakeSnapshot"/>.</summary>
    public MeterSnapshot Snapshot => _snapshot;

    /// <summary>
    /// The meter view since the last call: peak level and the most significant tag over every frame in between.
    /// Chunks hold several frames and the UI polls on its own schedule, so the latest frame alone would hide short clicks.
    /// </summary>
    public MeterSnapshot TakeSnapshot()
    {
        lock (_gate)
        {
            if (!_framesSince) return _lastTaken;
            var s = _lastTaken = _snapshot with { Dbfs = _peakSince, Tag = _tagSince };
            _peakSince = Dsp.MinDb;
            _tagSince = FrameTag.Quiet;
            _framesSince = false;
            return s;
        }
    }

    private static int Rank(FrameTag t) => t switch { FrameTag.Active => 3, FrameTag.Metronome => 2, FrameTag.Loud => 1, _ => 0 };
    /// <summary>The calibrated floor, or null if Calibrate hasn't run on this pipeline (or the one it was seeded from).</summary>
    public NoiseCalibration? Calibration => _detector.Floor.Calibrated ? new NoiseCalibration(_detector.Floor.Floor, _detector.Floor.Bias) : null;

    public GuidedLearnSession? Learn { get { lock (_gate) return _learn; } }
    public bool AutoLearnEnabled { get => _auto.Enabled; set => _auto.Enabled = value; }

    /// <summary>Raised on the audio thread when a guided learn ends, whatever the outcome.</summary>
    public event Action<GuidedLearnSession>? LearnFinished;

    public void StartLearn()
    {
        lock (_gate)
        {
            // Frames are not reported to the clock while learning, so end any run now rather than bridge the gap.
            // Inside the lock so no frame can slip a new pending start in between.
            _clock.Interrupt();
            _learn = new GuidedLearnSession(_bank, _now);
            _learnReported = false;
        }
    }

    public void CancelLearn()
    {
        lock (_gate) _learn?.Cancel();
    }

    public void StartCalibration()
    {
        lock (_gate)
        {
            _clock.Interrupt();
            _calibration = new List<double>();
            _snapshot = _snapshot with { Calibrating = true };
        }
    }

    public void Push(ReadOnlySpan<float> mono)
    {
        GuidedLearnSession? finished = null;
        lock (_gate)
        {
            _slicer.Push(mono, frame =>
            {
                _now = frame.End;
                var sample = _detector.Process(frame);
                var learning = _learn is { IsDone: false };
                _learn?.OnFrame(_now, _detector.SustainedOpen, frame.Duration);
                if (_learn is { IsDone: true } && !_learnReported) { _learnReported = true; finished = _learn; }
                if (!learning && _calibration == null) _clock.OnActivity(sample.Time, sample.Active);
                Calibrate(sample.Dbfs);
                _last = sample;
                _peakSince = Math.Max(_peakSince, sample.Dbfs);
                if (Rank(sample.Tag) > Rank(_tagSince)) _tagSince = sample.Tag;
                _framesSince = true;
            });
            // One snapshot per chunk, not per 25 ms frame: only the latest is ever read.
            if (_framesSince) _snapshot = new MeterSnapshot(_last.Dbfs, _detector.Floor.Floor, _detector.ThresholdDb, _last.Tag,
                _detector.Periodicity.CurrentBpm ?? _learn?.Bpm, _calibration != null);
        }
        if (finished != null) LearnFinished?.Invoke(finished);
    }

    private void Calibrate(double dbfs)
    {
        if (_calibration == null) return;
        _calibration.Add(dbfs);
        if (_calibration.Count * FrameLength < CalibrationLength) return;
        _detector.Floor.Calibrate(_calibration);
        _calibration = null;
    }
}
