using PracticeTimer.Core.Tests.Support;
using PracticeTimer.Core.Timing;

namespace PracticeTimer.Core.Tests.Timing;

public class PracticeClockAutoTests
{
    private const double Step = 0.05;
    private readonly FakeClock _time = new();
    private double _t;

    private PracticeClock Make() => new(_time) { StartDelay = TimeSpan.FromSeconds(1), StopDelay = TimeSpan.FromSeconds(5) };

    /// <summary>Feeds activity samples for the given number of seconds.</summary>
    private void Feed(PracticeClock c, double seconds, bool active)
    {
        var n = (int)Math.Round(seconds / Step);
        for (var i = 0; i < n; i++) { c.OnActivity(TimeSpan.FromSeconds(_t), active); _t += Step; }
    }

    [Fact]
    public void StartAutoWaitsForSound()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, false);
        Assert.Equal(ClockState.Listening, c.State);
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
    }

    [Fact]
    public void SpikeShorterThanStartDelayIsIgnored()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 0.5, true);
        Feed(c, 2, false);
        Assert.Equal(ClockState.Listening, c.State);
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
    }

    [Fact]
    public void SustainedSoundEntersPlayingAndCreditsStartDelay()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, true);
        Assert.Equal(ClockState.Playing, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 2.9, 3.0);
    }

    [Fact]
    public void GapShorterThanStopDelayKeepsRunning()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, true);
        Feed(c, 3, false);
        Feed(c, 2, true);
        Assert.Equal(ClockState.Playing, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 7.9, 8.0);
    }

    [Fact]
    public void GapLongerThanStopDelayPausesAndRemovesTrailingQuiet()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 10, true);
        Feed(c, 6, false);
        Assert.Equal(ClockState.PausedQuiet, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 9.9, 10.0);
        Feed(c, 10, false);
        Assert.InRange(c.Elapsed.TotalSeconds, 9.9, 10.0);
    }

    [Fact]
    public void ResumesAfterPauseAndAccumulates()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 10, true);
        Feed(c, 6, false);
        Feed(c, 4, true);
        Assert.Equal(ClockState.Playing, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 13.9, 14.0);
    }

    [Fact]
    public void ManualPauseDisarmsListener()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, true);
        c.Pause();
        var e = c.Elapsed;
        Feed(c, 5, true);
        Assert.Equal(ClockState.ManualPaused, c.State);
        Assert.Equal(e, c.Elapsed);
    }

    [Fact]
    public void StartAutoAfterPauseResumesListeningKeepingTime()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, true);
        c.Pause();
        var e = c.Elapsed;
        c.StartAuto();
        Assert.Equal(ClockState.Listening, c.State);
        Assert.Equal(e, c.Elapsed);
    }

    [Fact]
    public void ResetClearsAutoState()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 3, true);
        c.Reset();
        Assert.Equal(ClockState.Stopped, c.State);
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
        Feed(c, 3, true);
        Assert.Equal(ClockState.Stopped, c.State);
    }

    [Fact]
    public void GoalReachedFiresInAutoMode()
    {
        var c = Make();
        var fired = 0;
        c.Goal = TimeSpan.FromSeconds(5);
        c.GoalReached += () => fired++;
        c.StartAuto();
        Feed(c, 8, true);
        c.Tick();
        c.Tick();
        Assert.Equal(1, fired);
    }
}
