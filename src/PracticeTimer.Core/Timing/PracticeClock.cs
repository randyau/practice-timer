using PracticeTimer.Core.Time;

namespace PracticeTimer.Core.Timing;

public enum ClockState { Stopped, ManualRunning, ManualPaused }

public sealed class PracticeClock
{
    private readonly IMonotonicClock _time;
    private readonly object _gate = new();
    private TimeSpan _committed;
    private TimeSpan _runStart;
    private bool _goalFired;

    public PracticeClock(IMonotonicClock time) => _time = time;

    public ClockState State { get; private set; } = ClockState.Stopped;
    public TimeSpan? Goal { get; set; }

    public event Action<ClockState>? StateChanged;
    public event Action? GoalReached;

    public TimeSpan Elapsed
    {
        get { lock (_gate) return ElapsedUnlocked(); }
    }

    private TimeSpan ElapsedUnlocked() =>
        State == ClockState.ManualRunning ? _committed + (_time.Now - _runStart) : _committed;

    public void Start()
    {
        lock (_gate)
        {
            if (State == ClockState.ManualRunning) return;
            _runStart = _time.Now;
            SetState(ClockState.ManualRunning);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State != ClockState.ManualRunning) return;
            _committed = ElapsedUnlocked();
            SetState(ClockState.ManualPaused);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _committed = TimeSpan.Zero;
            _goalFired = false;
            SetState(ClockState.Stopped);
        }
    }

    /// <summary>Call periodically; raises GoalReached once when the goal is crossed.</summary>
    public void Tick()
    {
        bool fire;
        lock (_gate)
        {
            fire = Goal is { } g && !_goalFired && ElapsedUnlocked() >= g;
            if (fire) _goalFired = true;
        }
        if (fire) GoalReached?.Invoke();
    }

    private void SetState(ClockState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }
}
