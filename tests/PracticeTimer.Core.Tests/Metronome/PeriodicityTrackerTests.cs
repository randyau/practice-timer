using PracticeTimer.Core.Metronome;

namespace PracticeTimer.Core.Tests.Metronome;

public class PeriodicityTrackerTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static List<bool> Feed(PeriodicityTracker p, IEnumerable<double> onsets) =>
        onsets.Select(o => p.Observe(S(o))).ToList();

    private static IEnumerable<double> Regular(double bpm, int n, double start = 0) =>
        Enumerable.Range(0, n).Select(i => start + i * 60.0 / bpm);

    [Theory]
    [InlineData(40)] [InlineData(60)] [InlineData(120)] [InlineData(208)]
    public void LocksAfterFourTicks(double bpm)
    {
        var p = new PeriodicityTracker();
        var r = Feed(p, Regular(bpm, 8));
        Assert.Equal(new[] { false, false, false, true, true, true, true, true }, r);
        Assert.Equal(bpm, p.CurrentBpm!.Value, 0);
    }

    [Fact]
    public void IrregularOnsetsNeverLock()
    {
        var p = new PeriodicityTracker();
        var onsets = new[] { 0, 0.5, 1.4, 1.8, 2.9, 3.5, 4.3, 5.6, 6.0, 7.2 };
        Assert.All(Feed(p, onsets), Assert.False);
        Assert.Null(p.CurrentBpm);
    }

    [Theory]
    [InlineData(20)] [InlineData(300)]
    public void OutOfRangeTempoDoesNotLock(double bpm)
    {
        var p = new PeriodicityTracker();
        Assert.All(Feed(p, Regular(bpm, 8)), Assert.False);
    }

    [Fact]
    public void MissedBeatStaysOnGrid()
    {
        var p = new PeriodicityTracker();
        Feed(p, Regular(100, 6));
        var next = 6 * 0.6 + 0.6; // skipped one beat
        Assert.True(p.Observe(S(next)));
    }

    [Fact]
    public void OffGridBurstIsNotAccepted()
    {
        var p = new PeriodicityTracker();
        Feed(p, Regular(60, 6));
        Assert.False(p.Observe(S(5 + 0.37)));
    }

    [Fact]
    public void TempoStepRelocksAndUpdatesBpm()
    {
        var p = new PeriodicityTracker();
        Feed(p, Regular(60, 6));
        var t0 = 5.0;
        var r = Feed(p, Regular(90, 8, t0 + 60.0 / 90));
        Assert.True(r.TakeLast(3).All(x => x));
        Assert.Equal(90, p.CurrentBpm!.Value, 0);
    }

    [Fact]
    public void GradualAccelerandoStaysLocked()
    {
        var p = new PeriodicityTracker();
        var t = 0.0; var bpm = 60.0; var results = new List<bool>();
        for (var i = 0; i < 30; i++) { results.Add(p.Observe(S(t))); t += 60 / bpm; bpm *= 1.03; }
        Assert.True(results.Skip(4).All(x => x));
    }

    [Fact]
    public void LongSilenceReleasesTheLock()
    {
        var p = new PeriodicityTracker();
        Feed(p, Regular(60, 6));
        Assert.False(p.Observe(S(5 + 20)));
        Assert.Null(p.CurrentBpm);
    }
}
