using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Tests.Support;
using static PracticeTimer.Core.Tests.Signals.SignalGen;

namespace PracticeTimer.Core.Tests.Metronome;

public class GuidedLearnTests
{
    private static GuidedLearnSession Learn(float[] audio, out Harness h, FingerprintBank? bank = null)
    {
        h = new Harness(bank);
        var s = h.StartGuided(TimeSpan.Zero);
        h.Run(audio);
        return s;
    }

    [Theory]
    [InlineData(60)] [InlineData(120)]
    public void LearnsSteadyClicks(double bpm)
    {
        var s = Learn(ClickTrain(bpm, 20, () => Click(-20)), out var h);
        Assert.Equal(LearnStatus.Success, s.Status);
        Assert.Equal(bpm, s.Bpm!.Value, 0);
        Assert.True(s.Result!.Locked);
        Assert.Single(h.Bank.Profiles);
    }

    [Fact]
    public void LearnsBeeps() =>
        Assert.Equal(LearnStatus.Success, Learn(ClickTrain(100, 15, () => Beep()), out _).Status);

    [Fact]
    public void LearnsTickTockAsTwoClusters()
    {
        var s = Learn(ClickTrain(_ => 90, 20, k => k % 2 == 0 ? Click(-20, 2500) : Click(-20, 900)), out _);
        Assert.True(s.TickCount > 0, $"ticks {s.TickCount} status {s.Status}");
        Assert.Equal(LearnStatus.Success, s.Status);
        Assert.Equal(2, s.Result!.Clusters.Count);
    }

    [Fact]
    public void ReportsProgressWhileLearning()
    {
        var s = Learn(ClickTrain(60, 3.5, () => Click(-20)), out _);
        Assert.Equal(LearnStatus.Learning, s.Status);
        Assert.InRange(s.TickCount, 2, 4);
    }

    [Fact]
    public void SilenceTimesOutAndStoresNothing()
    {
        var s = Learn(Silence(22), out var h);
        Assert.Equal(LearnStatus.Timeout, s.Status);
        Assert.Empty(h.Bank.Profiles);
    }

    [Fact]
    public void SustainedSoundIsRejected()
    {
        var audio = Mix(ClickTrain(60, 20, () => Click(-20)), Concat(Silence(1), Note(440, 4, -25), Silence(15)));
        var s = Learn(audio, out var h);
        Assert.Equal(LearnStatus.TooMuchSound, s.Status);
        Assert.Empty(h.Bank.Profiles);
    }

    [Fact]
    public void IrregularClicksDoNotSucceed()
    {
        var rnd = new Random(5);
        var parts = new List<float[]>();
        for (var i = 0; i < 30; i++) { parts.Add(Click(-20)); parts.Add(Silence(0.2 + rnd.NextDouble() * 1.2)); }
        Assert.NotEqual(LearnStatus.Success, Learn(Concat(parts.ToArray()), out _).Status);
    }

    [Fact]
    public void LearningAKnownMetronomeReportsAlreadyKnown()
    {
        var bank = new FingerprintBank();
        Learn(ClickTrain(80, 15, () => Click(-20)), out _, bank);
        var s = Learn(ClickTrain(110, 15, () => Click(-20)), out _, bank);
        Assert.Equal(LearnStatus.AlreadyKnown, s.Status);
        Assert.Single(bank.Profiles);
    }

    [Fact]
    public void TeachingAnAutoLearnedMetronomePromotesItAsSuccess()
    {
        var bank = new FingerprintBank();
        new Harness(bank, autoLearn: true).Run(ClickTrain(90, 15, () => Click(-20)));
        Assert.False(Assert.Single(bank.Profiles).Locked);
        var s = Learn(ClickTrain(100, 15, () => Click(-20)), out _, bank);
        Assert.Equal(LearnStatus.Success, s.Status);
        Assert.True(Assert.Single(bank.Profiles).Locked);
    }

    [Fact]
    public void CancelStopsLearning()
    {
        var h = new Harness();
        var s = h.StartGuided(TimeSpan.Zero);
        s.Cancel();
        h.Run(ClickTrain(60, 15, () => Click(-20)));
        Assert.Equal(LearnStatus.Cancelled, s.Status);
        Assert.Empty(h.Bank.Profiles);
    }
}

public class LearnedFilterTests
{
    private static FingerprintBank LearnedBank(Func<float[]> click)
    {
        var bank = new FingerprintBank();
        var h = new Harness(bank);
        h.StartGuided(TimeSpan.Zero);
        h.Run(ClickTrain(80, 15, click));
        Assert.Single(bank.Profiles);
        return bank;
    }

    private static float[] IrregularBeeps(int seed, double sec)
    {
        var rnd = new Random(seed);
        var parts = new List<float[]>();
        for (var t = 0.0; t < sec;) { parts.Add(Beep(1000, 100)); var g = 0.15 + rnd.NextDouble() * 0.15; parts.Add(Silence(g)); t += 0.1 + g; }
        return Concat(parts.ToArray());
    }

    [Fact]
    public void WithoutLearningDenseIrregularBeepsCountAsActivity()
    {
        var h = new Harness();
        Assert.True(DetectorRun.ActiveSeconds(h.Run(IrregularBeeps(1, 12))) > 2);
    }

    [Fact]
    public void LearnedSoundIsIgnoredEvenOffTheGrid()
    {
        var h = new Harness(LearnedBank(() => Beep(1000, 100)));
        Assert.Equal(0, DetectorRun.ActiveSeconds(h.Run(IrregularBeeps(1, 12))), 0.3);
    }

    [Fact]
    public void LearnedMetronomeIgnoredAtNewTempoWithoutRelearning()
    {
        var h = new Harness(LearnedBank(() => Click(-20)));
        var s = h.Run(ClickTrain(t => 50 + t * 8, 20, _ => Click(-20)));
        Assert.Equal(0, DetectorRun.ActiveSeconds(s), 0.1);
    }

    [Fact]
    public void LearnedMetronomeDoesNotHideNotes()
    {
        var h = new Harness(LearnedBank(() => Click(-20)));
        var audio = Mix(ClickTrain(95, 12, () => Click(-20)), Concat(Silence(2), Note(440, 5, -25), Silence(5)));
        Assert.InRange(DetectorRun.ActiveSeconds(h.Run(audio), 2, 7.5), 4.5, 5.7);
    }

    [Fact]
    public void LearnedMetronomeStillIgnoredWhenItStartsBeforeAnyPlaying()
    {
        var h = new Harness(LearnedBank(() => Click(-20)));
        var audio = Mix(ClickTrain(70, 20, () => Click(-20)), Concat(Silence(10), Note(440, 3, -25), Silence(7)));
        var s = h.Run(audio);
        Assert.Equal(0, DetectorRun.ActiveSeconds(s, 0, 9.8));
        Assert.Equal(0, DetectorRun.ActiveSeconds(s, 14));
    }
}

public class AutoLearnTests
{
    [Fact]
    public void LearnsSteadyClicksAsTentative()
    {
        var h = new Harness(autoLearn: true);
        h.Run(ClickTrain(90, 15, () => Click(-20)));
        var p = Assert.Single(h.Bank.Profiles);
        Assert.False(p.Locked);
    }

    [Fact]
    public void DoesNothingWhenDisabled()
    {
        var h = new Harness(autoLearn: false);
        h.Run(ClickTrain(90, 15, () => Click(-20)));
        Assert.Empty(h.Bank.Profiles);
    }

    [Fact]
    public void DoesNotLearnSteadyPizzicato()
    {
        var h = new Harness(autoLearn: true);
        var pluck = () => { var t = Tone(440, 0.4, -20); for (var i = 0; i < t.Length; i++) t[i] *= (float)Math.Exp(-i / (Rate * 0.15)); return t; };
        h.Run(ClickTrain(90, 15, pluck));
        Assert.Empty(h.Bank.Profiles);
    }

    [Fact]
    public void DoesNotLearnIrregularSounds()
    {
        var rnd = new Random(2);
        var parts = new List<float[]>();
        for (var i = 0; i < 25; i++) { parts.Add(Click(-20)); parts.Add(Silence(0.2 + rnd.NextDouble() * 1.3)); }
        var h = new Harness(autoLearn: true);
        h.Run(Concat(parts.ToArray()));
        Assert.Empty(h.Bank.Profiles);
    }
}
