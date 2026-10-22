using PracticeTimer.Core.Time;

namespace PracticeTimer.Core.Timing;

public enum ClockState { Stopped, ManualRunning, ManualPaused, Listening, Playing, PausedQuiet }

public sealed class PracticeClock
{
    private readonly IMonotonicClock _time;
    private readonly object _gate = new();
    private TimeSpan _committed;
    private TimeSpan _runStart;
    private bool _goalFired;
    private TimeSpan _lastSample;
    private TimeSpan _lastActive;
    private TimeSpan? _candidateStart;

    public PracticeClock(IMonotonicClock time) => _time = time;

    public ClockState State { get; private set; } = ClockState.Stopped;
    private TimeSpan? _goal;

    public TimeSpan? Goal
    {
        get => _goal;
        set
        {
            lock (_gate) { _goal = value; _goalFired = false; }
        }
    }
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan StopDelay { get; set; } = TimeSpan.FromSeconds(5);

    public event Action<ClockState>? StateChanged;
    public event Action? GoalReached;

    public TimeSpan Elapsed
    {
        get { lock (_gate) return ElapsedUnlocked(); }
    }

    private TimeSpan ElapsedUnlocked() =>
        State switch
        {
            ClockState.ManualRunning => _committed + (_time.Now - _runStart),
            ClockState.Playing => _committed + (_lastSample - _runStart),
            _ => _committed,
        };

    public void Start()
    {
        lock (_gate)
        {
            if (State == ClockState.ManualRunning) return;
            _runStart = _time.Now;
            SetState(ClockState.ManualRunning);
        }
    }

    /// <summary>Arms the listener; the clock runs only while activity is reported.</summary>
    public void StartAuto()
    {
        lock (_gate)
        {
            if (State is ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet) return;
            if (State == ClockState.ManualRunning) _committed = ElapsedUnlocked();
            _candidateStart = null;
            SetState(ClockState.Listening);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State is ClockState.Stopped or ClockState.ManualPaused) return;
            _committed = ElapsedUnlocked();
            SetState(ClockState.ManualPaused);
        }
    }

    /// <summary>Feeds one activity sample; <paramref name="time"/> is the sample's own timestamp.</summary>
    public void OnActivity(TimeSpan time, bool active)
    {
        lock (_gate)
        {
            if (State is not (ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet)) return;
            _lastSample = time;
            if (State == ClockState.Playing)
            {
                if (active) _lastActive = time;
                else if (time - _lastActive >= StopDelay)
                {
                    _committed += _lastActive - _runStart;
                    _candidateStart = null;
                    SetState(ClockState.PausedQuiet);
                }
                return;
            }
            if (!active) { _candidateStart = null; return; }
            _candidateStart ??= time;
            if (time - _candidateStart.Value >= StartDelay)
            {
                _runStart = _candidateStart.Value;
                _lastActive = time;
                SetState(ClockState.Playing);
            }
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
            fire = _goal is { } g && !_goalFired && ElapsedUnlocked() >= g;
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
