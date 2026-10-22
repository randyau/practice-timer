using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Tests.Signals;

namespace PracticeTimer.Core.Tests.Analysis;

public class DspTests
{
    [Fact]
    public void FullScaleSineIsMinus3Db() =>
        Assert.Equal(-3.01, Dsp.Dbfs(SignalGen.Tone(1000, 1, -3.01)), 1);

    [Fact]
    public void SilenceClampsToFloor() => Assert.Equal(Dsp.MinDb, Dsp.Dbfs(new float[400]));

    [Fact]
    public void ToneLevelIsMeasuredAccurately() =>
        Assert.Equal(-30, Dsp.Dbfs(SignalGen.Tone(440, 0.2, -30)), 0);
}

public class FrameSlicerTests
{
    [Fact]
    public void EmitsFixedFramesWithSampleBasedTimestamps()
    {
        var s = new FrameSlicer(SignalGen.Rate, TimeSpan.FromMilliseconds(25));
        var frames = new List<AudioFrame>();
        foreach (var chunk in SignalGen.Chunks(new float[16000], 333)) s.Push(chunk, frames.Add);
        Assert.Equal(40, frames.Count);
        Assert.All(frames, f => Assert.Equal(400, f.Samples.Length));
        for (var i = 0; i < frames.Count; i++)
            Assert.Equal(TimeSpan.FromMilliseconds(25 * i), frames[i].Start);
    }

    [Fact]
    public void PreservesSampleOrderAcrossChunkBoundaries()
    {
        var s = new FrameSlicer(1000, TimeSpan.FromMilliseconds(10));
        var data = Enumerable.Range(0, 30).Select(i => (float)i).ToArray();
        var frames = new List<AudioFrame>();
        s.Push(data.AsSpan(0, 7), frames.Add);
        s.Push(data.AsSpan(7), frames.Add);
        Assert.Equal(data, frames.SelectMany(f => f.Samples).ToArray());
    }
}

public class NoiseFloorEstimatorTests
{
    [Fact]
    public void UsesDefaultUntilEnoughSamples()
    {
        var n = new NoiseFloorEstimator();
        Assert.Equal(NoiseFloorEstimator.DefaultFloorDb, n.Floor);
        for (var i = 0; i < 5; i++) n.Add(-80);
        Assert.Equal(NoiseFloorEstimator.DefaultFloorDb, n.Floor);
    }

    [Fact]
    public void FloorIsLowPercentileOfQuietFrames()
    {
        var n = new NoiseFloorEstimator();
        for (var i = 0; i < 100; i++) n.Add(-70 + i % 10); // -70..-61
        Assert.InRange(n.Floor, -70, -68);
    }

    [Fact]
    public void OldFramesAgeOut()
    {
        var n = new NoiseFloorEstimator(windowFrames: 50);
        for (var i = 0; i < 50; i++) n.Add(-80);
        for (var i = 0; i < 50; i++) n.Add(-60);
        Assert.Equal(-60, n.Floor, 1);
    }

    [Fact]
    public void CalibrateOverridesFloor()
    {
        var n = new NoiseFloorEstimator();
        for (var i = 0; i < 100; i++) n.Add(-80);
        n.Calibrate(Enumerable.Repeat(-50.0, 100).ToList());
        Assert.Equal(-50, n.Floor, 1);
    }
}

public class LoudnessGateTests
{
    private static LoudnessGate Make() => new() { MarginDb = 12, AbsoluteMinDb = -55 };

    [Fact]
    public void NeedsMarginAboveFloor()
    {
        var g = Make();
        Assert.False(g.Process(-45, floorDb: -40));
        Assert.True(g.Process(-25, floorDb: -40));
    }

    [Fact]
    public void AbsoluteMinimumApplies()
    {
        var g = Make();
        Assert.False(g.Process(-60, floorDb: -90));
        Assert.True(g.Process(-50, floorDb: -90));
    }

    [Fact]
    public void HysteresisKeepsGateOpenSlightlyBelowEnterThreshold()
    {
        var g = Make();
        Assert.True(g.Process(-50, floorDb: -90));   // enter at -55
        Assert.True(g.Process(-57, floorDb: -90));   // exit at -58
        Assert.False(g.Process(-59, floorDb: -90));
        Assert.False(g.Process(-56, floorDb: -90));  // must exceed enter again
    }
}

public class BandSpectrumTests
{
    private readonly BandSpectrum _spec = new(SignalGen.Rate, 400);

    private double[] Of(float[] a) => _spec.Compute(a.AsSpan(0, 400).ToArray());

    [Fact]
    public void SameSignalDifferentLevelGivesSameShape() =>
        Assert.True(BandSpectrum.Cosine(Of(SignalGen.Note(800, 0.1, -20)), Of(SignalGen.Note(800, 0.1, -40))) > 0.999);

    [Fact]
    public void SimilarTimbreScoresHigherThanDifferentOne()
    {
        var a = Of(SignalGen.Tone(1000, 0.1, -20));
        var near = Of(SignalGen.Tone(1050, 0.1, -20));
        var far = Of(SignalGen.Tone(4000, 0.1, -20));
        Assert.True(BandSpectrum.Cosine(a, near) > 0.5);
        Assert.True(BandSpectrum.Cosine(a, far) < 0.3);
    }

    [Fact]
    public void EnergyLandsInExpectedBand()
    {
        var lo = Of(SignalGen.Tone(200, 0.1, -20));
        var hi = Of(SignalGen.Tone(5000, 0.1, -20));
        Assert.True(Array.IndexOf(lo, lo.Max()) < Array.IndexOf(hi, hi.Max()));
    }

    [Fact]
    public void ResultIsUnitLength() =>
        Assert.Equal(1.0, Math.Sqrt(Of(SignalGen.Tone(700, 0.1, -20)).Sum(x => x * x)), 6);

    [Fact]
    public void MergeIsWeightedAndNormalised()
    {
        var a = Of(SignalGen.Tone(300, 0.1, -20));
        var b = Of(SignalGen.Tone(3000, 0.1, -20));
        var m = BandSpectrum.Merge(a, 3, b, 1);
        Assert.Equal(1.0, Math.Sqrt(m.Sum(x => x * x)), 6);
        Assert.True(BandSpectrum.Cosine(m, a) > BandSpectrum.Cosine(m, b));
    }
}
