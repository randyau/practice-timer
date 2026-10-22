using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;

namespace PracticeTimer.Core.Tests.Detection;

public class BurstTrackerTests
{
    private readonly BurstTracker _t = new();
    private int _i;
    private readonly List<Burst> _closed = new();

    private void Frames(int count, bool loud, double db = -20)
    {
        for (var n = 0; n < count; n++, _i++)
        {
            var f = new AudioFrame(TimeSpan.FromMilliseconds(25 * _i), new float[400], 16000);
            var b = _t.Update(f, loud, db);
            if (b != null) _closed.Add(b);
        }
    }

    [Fact]
    public void ShortBurstReportsOnsetDurationAndPeak()
    {
        Frames(8, false);
        Frames(2, true, -30); Frames(1, true, -20);
        Frames(3, false);
        var b = Assert.Single(_closed);
        Assert.Equal(TimeSpan.FromMilliseconds(200), b.Onset);
        Assert.Equal(TimeSpan.FromMilliseconds(75), b.Duration);
        Assert.Equal(-20, b.PeakDb);
        Assert.False(b.Sustained);
    }

    [Fact]
    public void GapBeforeIsQuietTimeSincePreviousBurst()
    {
        Frames(1, true); Frames(10, false); Frames(1, true); Frames(3, false);
        Assert.Equal(2, _closed.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(250), _closed[1].GapBefore);
    }

    [Fact]
    public void SingleQuietFrameDoesNotSplitBurst()
    {
        Frames(4, true); Frames(1, false); Frames(4, true); Frames(3, false);
        var b = Assert.Single(_closed);
        Assert.Equal(TimeSpan.FromMilliseconds(225), b.Duration);
    }

    [Fact]
    public void LongBurstIsSustainedAndOpenDurationExposed()
    {
        Frames(10, true);
        Assert.True(_t.OpenDuration > TimeSpan.FromMilliseconds(150));
        Assert.True(_t.IsSustained);
        Frames(3, false);
        Assert.True(Assert.Single(_closed).Sustained);
        Assert.False(_t.IsSustained);
    }
}
