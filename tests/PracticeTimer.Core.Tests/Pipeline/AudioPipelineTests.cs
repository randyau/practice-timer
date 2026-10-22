using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Pipeline;
using PracticeTimer.Core.Tests.Support;
using PracticeTimer.Core.Timing;
using static PracticeTimer.Core.Tests.Signals.SignalGen;

namespace PracticeTimer.Core.Tests.Pipeline;

public class AudioPipelineTests
{
    private readonly PracticeClock _clock = new(new FakeClock());
    private readonly FingerprintBank _bank = new();
    private readonly AudioPipeline _p;

    public AudioPipelineTests() => _p = new AudioPipeline(Rate, _clock, new DetectorSettings(), _bank);

    private void Feed(float[] audio, bool room = true)
    {
        foreach (var c in Chunks(room ? WithRoom(audio) : audio, 480)) _p.Push(c);
    }

    [Fact]
    public void PlayingDrivesTheClockAndTrailingQuietIsRemoved()
    {
        _clock.StartAuto();
        Feed(Concat(Silence(2), Note(440, 5, -25), Silence(8)));
        Assert.Equal(ClockState.PausedQuiet, _clock.State);
        Assert.InRange(_clock.Elapsed.TotalSeconds, 4.6, 5.8);
    }

    [Fact]
    public void MetronomeAloneNeverStartsTheClock()
    {
        _clock.StartAuto();
        Feed(ClickTrain(90, 20, () => Click(-20)));
        Assert.Equal(ClockState.Listening, _clock.State);
        Assert.Equal(TimeSpan.Zero, _clock.Elapsed);
    }

    [Fact]
    public void MeterSnapshotReportsLevelThresholdAndTempo()
    {
        Feed(ClickTrain(90, 10, () => Click(-20)));
        var s = _p.Snapshot;
        Assert.True(s.Dbfs < -40);
        Assert.True(s.ThresholdDb > s.FloorDb);
        Assert.InRange(s.Bpm ?? 0, 88, 92);
    }

    [Fact]
    public void GuidedLearnSavesMetronomeAndReportsCompletion()
    {
        var done = new List<LearnStatus>();
        _p.LearnFinished += s => done.Add(s.Status);
        Feed(Silence(1));
        _p.StartLearn();
        Assert.Equal(LearnStatus.Learning, _p.Learn!.Status);
        Feed(ClickTrain(80, 15, () => Click(-20)));
        Assert.Equal(new[] { LearnStatus.Success }, done);
        Assert.Single(_bank.Profiles);
    }

    [Fact]
    public void ClockIgnoresSoundWhileLearning()
    {
        _clock.StartAuto();
        _p.StartLearn();
        Feed(Concat(Silence(1), Note(440, 0.9, -25)));
        Assert.Equal(TimeSpan.Zero, _clock.Elapsed);
        Assert.Equal(LearnStatus.Learning, _p.Learn!.Status);
        Feed(Note(440, 2, -25), room: false);
        Assert.Equal(LearnStatus.TooMuchSound, _p.Learn!.Status);
    }

    [Fact]
    public void CancelLearnResumesNormalOperation()
    {
        _clock.StartAuto();
        _p.StartLearn();
        _p.CancelLearn();
        Feed(Concat(Silence(1), Note(440, 3, -25), Silence(1)));
        Assert.True(_clock.Elapsed > TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AutoLearnToggleControlsTentativeProfiles()
    {
        _p.AutoLearnEnabled = false;
        Feed(ClickTrain(90, 12, () => Click(-20)));
        Assert.Empty(_bank.Profiles);
        _p.AutoLearnEnabled = true;
        Feed(ClickTrain(90, 12, () => Click(-20)));
        Assert.Single(_bank.Profiles);
    }

    [Fact]
    public void CalibrationSetsFloorFromRoomNoise()
    {
        _p.StartCalibration();
        Assert.True(_p.Snapshot.Calibrating);
        Feed(Silence(4, noiseDb: -45), room: false);
        Assert.False(_p.Snapshot.Calibrating);
        Assert.InRange(_p.Snapshot.FloorDb, -47, -43);
    }

    [Fact]
    public void LearningDoesNotCreditTheGapToTheClock()
    {
        _clock.StartAuto();
        Feed(Concat(Silence(1), Note(440, 4, -25)));
        _p.StartLearn();
        Assert.Equal(ClockState.PausedQuiet, _clock.State);
        var before = _clock.Elapsed;
        Feed(Silence(10));
        _p.CancelLearn();
        Feed(Concat(Note(440, 3, -25), Silence(7)));
        Assert.InRange((_clock.Elapsed - before).TotalSeconds, 2.0, 3.6);
    }

    [Fact]
    public void TakeSnapshotKeepsPeakAndStrongestTagSinceLastRead()
    {
        Feed(Silence(1));
        _p.TakeSnapshot();
        Feed(Concat(Silence(0.1), Click(-20), Silence(0.5)));
        var s = _p.TakeSnapshot();
        Assert.True(s.Dbfs > -45, $"peak was {s.Dbfs}");
        Assert.NotEqual(FrameTag.Quiet, s.Tag);
        var again = _p.TakeSnapshot();
        Assert.Equal(s.Dbfs, again.Dbfs); // no new frames: unchanged
        Feed(Silence(0.5));
        Assert.True(_p.TakeSnapshot().Dbfs < -60);
    }

    [Fact]
    public void TempoReadoutClearsAfterTheMetronomeStops()
    {
        Feed(ClickTrain(90, 10, () => Click(-20)));
        Assert.NotNull(_p.Snapshot.Bpm);
        Feed(Silence(5));
        Assert.Null(_p.Snapshot.Bpm);
    }

    [Fact]
    public void CalibrationHoldsAcrossVariedRoomNoiseAfterwards()
    {
        _p.StartCalibration();
        var noisy = Concat(Silence(2, noiseDb: -60, seed: 2), Silence(2, noiseDb: -48, seed: 3));
        Feed(noisy, room: false);
        var floor = _p.Snapshot.FloorDb;
        Feed(Silence(1, noiseDb: -60, seed: 4), room: false);
        Assert.InRange(_p.Snapshot.FloorDb, floor - 1, floor + 1);
    }
}
