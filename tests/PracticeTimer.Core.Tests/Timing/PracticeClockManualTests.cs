using PracticeTimer.Core.Tests.Support;
using PracticeTimer.Core.Timing;

namespace PracticeTimer.Core.Tests.Timing;

public class PracticeClockManualTests
{
    private readonly FakeClock _time = new();
    private PracticeClock Make() => new(_time);

    [Fact]
    public void NewClockIsStoppedAtZero()
    {
        var c = Make();
        Assert.Equal(ClockState.Stopped, c.State);
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
    }

    [Fact]
    public void StartRunsAndAccumulates()
    {
        var c = Make();
        c.Start();
        _time.Advance(5);
        Assert.Equal(ClockState.ManualRunning, c.State);
        Assert.Equal(TimeSpan.FromSeconds(5), c.Elapsed);
    }

    [Fact]
    public void PauseFreezesElapsed()
    {
        var c = Make();
        c.Start();
        _time.Advance(5);
        c.Pause();
        _time.Advance(10);
        Assert.Equal(ClockState.ManualPaused, c.State);
        Assert.Equal(TimeSpan.FromSeconds(5), c.Elapsed);
    }

    [Fact]
    public void ResumeContinuesFromPausedValue()
    {
        var c = Make();
        c.Start(); _time.Advance(5); c.Pause(); _time.Advance(10);
        c.Start(); _time.Advance(2);
        Assert.Equal(TimeSpan.FromSeconds(7), c.Elapsed);
    }

    [Fact]
    public void ResetClearsAndStops()
    {
        var c = Make();
        c.Start(); _time.Advance(5);
        c.Reset();
        _time.Advance(5);
        Assert.Equal(ClockState.Stopped, c.State);
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
    }

    [Fact]
    public void StateChangedRaisedOnTransitions()
    {
        var c = Make();
        var seen = new List<ClockState>();
        c.StateChanged += seen.Add;
        c.Start(); c.Pause(); c.Reset();
        Assert.Equal(new[] { ClockState.ManualRunning, ClockState.ManualPaused, ClockState.Stopped }, seen);
    }

    [Fact]
    public void GoalReachedFiresOnceWhenCrossed()
    {
        var c = Make();
        var fired = 0;
        c.Goal = TimeSpan.FromSeconds(10);
        c.GoalReached += () => fired++;
        c.Start();
        _time.Advance(9); c.Tick();
        Assert.Equal(0, fired);
        _time.Advance(2); c.Tick();
        _time.Advance(5); c.Tick();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void GoalCanFireAgainAfterReset()
    {
        var c = Make();
        var fired = 0;
        c.Goal = TimeSpan.FromSeconds(1);
        c.GoalReached += () => fired++;
        c.Start(); _time.Advance(2); c.Tick();
        c.Reset();
        c.Start(); _time.Advance(2); c.Tick();
        Assert.Equal(2, fired);
    }
}
