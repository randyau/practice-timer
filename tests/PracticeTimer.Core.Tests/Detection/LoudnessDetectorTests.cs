using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Tests.Signals;
using PracticeTimer.Core.Tests.Support;
using static PracticeTimer.Core.Tests.Signals.SignalGen;

namespace PracticeTimer.Core.Tests.Detection;

public class LoudnessDetectorTests
{
    private static LoudnessDetector Make(Action<DetectorSettings>? tweak = null)
    {
        var s = new DetectorSettings();
        tweak?.Invoke(s);
        return new LoudnessDetector(Rate, s);
    }

    private static List<ActivitySample> Run(float[] audio, Action<DetectorSettings>? tweak = null) =>
        DetectorRun.Run(Make(tweak), WithRoom(audio));

    private static float[] Click20() => Click(-20);

    [Fact]
    public void SilenceIsNeverActive() =>
        Assert.Equal(0, DetectorRun.ActiveSeconds(Run(Silence(10))));

    [Fact]
    public void SustainedNoteBecomesActiveQuickly()
    {
        var s = Run(Concat(Silence(1), Note(440, 3, -25), Silence(3)));
        Assert.InRange(DetectorRun.FirstActive(s), 1.0, 1.4);
        Assert.InRange(DetectorRun.ActiveSeconds(s, 1, 4), 2.6, 3.0);
    }

    [Fact]
    public void ActivityEndsShortlyAfterNoteStops()
    {
        var s = Run(Concat(Silence(1), Note(440, 3, -25), Silence(4)));
        Assert.Equal(0, DetectorRun.ActiveSeconds(s, 4.8));
    }

    [Theory]
    [InlineData(40)] [InlineData(60)] [InlineData(120)] [InlineData(208)]
    public void MechanicalClicksAloneAreIgnored(double bpm) =>
        Assert.Equal(0, DetectorRun.ActiveSeconds(Run(ClickTrain(bpm, 20, Click20))), 0.2);

    [Theory]
    [InlineData(60)] [InlineData(90)] [InlineData(208)]
    public void DigitalBeepsAloneAreIgnored(double bpm) =>
        Assert.Equal(0, DetectorRun.ActiveSeconds(Run(ClickTrain(bpm, 20, () => Beep()))), 0.4);

    [Fact]
    public void LoneClapIsIgnored() =>
        Assert.Equal(0, DetectorRun.ActiveSeconds(Run(Concat(Silence(2), Click(-10), Silence(5)))));

    [Fact]
    public void PlayingOverClicksIsDetected()
    {
        var audio = Mix(ClickTrain(80, 12, Click20), Concat(Silence(2), Note(440, 6, -25), Silence(4)));
        var s = Run(audio);
        Assert.InRange(DetectorRun.ActiveSeconds(s, 2, 8.5), 5.5, 6.6);
    }

    [Fact]
    public void ClicksDuringARestDoNotHoldActivityOpen()
    {
        var audio = Mix(ClickTrain(80, 20, Click20), Concat(Silence(2), Note(440, 4, -25), Silence(14)));
        var s = Run(audio);
        Assert.Equal(0, DetectorRun.ActiveSeconds(s, 7.5));
    }

    [Fact]
    public void TempoChangesStayIgnored()
    {
        var audio = ClickTrain(t => t < 6 ? 60 : t < 12 ? 90 : 45, 24, _ => Click20());
        Assert.Equal(0, DetectorRun.ActiveSeconds(Run(audio)), 0.2);
    }

    [Fact]
    public void DisablingTheFilterCountsClicks()
    {
        var s = Run(ClickTrain(120, 10, Click20), x => x.MetronomeFilterEnabled = false);
        Assert.True(DetectorRun.ActiveSeconds(s) > 2);
    }

    [Fact]
    public void DenseIrregularShortNotesAreActive()
    {
        var rnd = new Random(3);
        var parts = new List<float[]> { Silence(1) };
        for (var i = 0; i < 14; i++) { parts.Add(Note(500, 0.12, -22)); parts.Add(Silence(0.15 + rnd.NextDouble() * 0.25)); }
        parts.Add(Silence(3));
        var s = Run(Concat(parts.ToArray()));
        Assert.True(DetectorRun.ActiveSeconds(s, 1, 6) > 2.5);
    }
}
