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

    [Fact]
    public void TimeRunningBackwardsEndsTheRunWithoutGoingNegative()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 600, true);
        _t = 0; // a new audio pipeline restarts its timestamps
        Feed(c, 2, true);
        Feed(c, 6, false);
        Assert.Equal(ClockState.PausedQuiet, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 598, 603);
    }

    [Fact]
    public void InterruptCommitsToLastActiveAndForgetsPendingStart()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 10, true);
        Feed(c, 2, false);
        c.Interrupt();
        Assert.Equal(ClockState.PausedQuiet, c.State);
        Assert.InRange(c.Elapsed.TotalSeconds, 9.9, 10.1);
        Feed(c, 0.5, true); // shorter than StartDelay: must not resume from an old candidate
        Assert.Equal(ClockState.PausedQuiet, c.State);
    }

    [Fact]
    public void InterruptedGapIsNotCreditedWhenPlayingResumes()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 0.5, true);
        c.Interrupt();
        _t += 20; // learn / calibration period, no samples reported
        Feed(c, 3, true);
        Assert.InRange(c.Elapsed.TotalSeconds, 2.9, 3.1);
    }

    [Fact]
    public void ManualPauseDropsTrailingQuiet()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 10, true);
        Feed(c, 3, false);
        c.Pause();
        Assert.InRange(c.Elapsed.TotalSeconds, 9.9, 10.1);
    }

    [Fact]
    public void StartFromAutoPlayingKeepsTheAutoTime()
    {
        var c = Make();
        c.StartAuto();
        Feed(c, 10, true);
        c.Start();
        Assert.InRange(c.Elapsed.TotalSeconds, 9.9, 10.1);
    }

    [Fact]
    public void GoalDoesNotFireOnTrailingQuiet()
    {
        var c = Make();
        var fired = 0;
        c.GoalReached += () => fired++;
        c.Goal = TimeSpan.FromSeconds(11);
        c.StartAuto();
        Feed(c, 10, true);
        Feed(c, 4, false);
        c.Tick();
        Assert.Equal(0, fired);
    }

    [Fact]
    public void StateChangedHandlerMayCallBackIntoTheClock()
    {
        var c = Make();
        TimeSpan seen = TimeSpan.MinValue;
        c.StateChanged += _ => { seen = c.Elapsed; };
        var t = Task.Run(() => { c.StartAuto(); Feed(c, 3, true); });
        Assert.True(t.Wait(2000));
        Assert.NotEqual(TimeSpan.MinValue, seen);
    }
}
