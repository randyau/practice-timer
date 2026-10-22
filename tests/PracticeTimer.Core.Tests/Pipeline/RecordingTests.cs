using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Tests.Support;

namespace PracticeTimer.Core.Tests.Pipeline;

/// <summary>
/// Real recordings through the real pipeline. <c>metronome.wav</c> is a phone metronome app at 60 BPM, picked up by a
/// microphone, with a clicking sound that rings for a few hundred milliseconds. <c>claps.wav</c> is irregular hand claps
/// standing in for music. Run <c>tools/Replay</c> on either file to see what the detector decides, frame by frame.
/// </summary>
public class RecordingTests
{
    private static (float[] Samples, int Rate) Load(string name) => WavReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static FingerprintBank TaughtMetronome()
    {
        var bank = new FingerprintBank();
        var (s, r) = Load("metronome.wav");
        Assert.Equal(LearnStatus.Success, ReplayRunner.Teach(s, r, bank)!.Status);
        return bank;
    }

    /// <summary>Claps from 5 s on laid over the metronome, as playing along with it.</summary>
    private static (float[] Samples, int Rate) ClapsOverMetronome()
    {
        var (m, rate) = Load("metronome.wav");
        var (c, _) = Load("claps.wav");
        var from = 5 * rate;
        return (m.Select((x, i) => Math.Clamp(x + c[from + i], -1f, 1f)).ToArray(), rate);
    }

    [Fact]
    public void GuidedLearnHearsThePhoneMetronomeAtItsTempo()
    {
        var (s, r) = Load("metronome.wav");
        var learn = ReplayRunner.Run(s, r, new FingerprintBank(), learnAt: 1.0).Learn;
        Assert.Equal(LearnStatus.Success, learn!.Status);
        Assert.InRange(learn.Bpm!.Value, 59, 61);
    }

    [Fact]
    public void MetronomeAloneIsNotCreditedEvenBeforeItIsLearned()
    {
        var (s, r) = Load("metronome.wav");
        Assert.True(ReplayRunner.Run(s, r, new FingerprintBank(), auto: true).CreditedSeconds < 1);
    }

    [Fact]
    public void LearnedMetronomeIsIgnoredFromTheFirstTick()
    {
        var (s, r) = Load("metronome.wav");
        var run = ReplayRunner.Run(s, r, TaughtMetronome(), auto: true);
        Assert.Equal(0, run.CreditedSeconds);
        Assert.DoesNotContain('A', run.Timeline.Substring(20)); // after the first couple of ticks, nothing reads as playing
    }

    [Fact]
    public void IrregularClapsCountAsPlaying()
    {
        var (s, r) = Load("claps.wav");
        var run = ReplayRunner.Run(s, r, new FingerprintBank(), auto: true);
        Assert.True(run.CreditedSeconds > 13, $"credited {run.CreditedSeconds:0.0} s of {run.Seconds:0.0} s");
    }

    [Fact]
    public void LearnedMetronomeDoesNotSwallowClaps()
    {
        var (s, r) = Load("claps.wav");
        var run = ReplayRunner.Run(s, r, TaughtMetronome(), auto: true);
        Assert.True(run.CreditedSeconds > 13, $"credited {run.CreditedSeconds:0.0} s of {run.Seconds:0.0} s");
    }

    [Fact]
    public void PlayingOverALearnedMetronomeStillCounts()
    {
        var (s, r) = ClapsOverMetronome();
        var run = ReplayRunner.Run(s, r, TaughtMetronome(), auto: true);
        Assert.True(run.CreditedSeconds > 6.5, $"credited {run.CreditedSeconds:0.0} s of {run.Seconds:0.0} s");
    }

    [Fact]
    public void PlayingOverAnUnlearnedMetronomeStillCounts()
    {
        var (s, r) = ClapsOverMetronome();
        var run = ReplayRunner.Run(s, r, new FingerprintBank(), auto: true);
        Assert.True(run.CreditedSeconds > 8, $"credited {run.CreditedSeconds:0.0} s of {run.Seconds:0.0} s");
    }
}
