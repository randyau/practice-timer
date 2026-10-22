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
        ClockState? changed;
        lock (_gate)
        {
            if (State == ClockState.ManualRunning) return;
            CommitAutoRun();
            _candidateStart = null;
            _runStart = _time.Now;
            changed = SetState(ClockState.ManualRunning);
        }
        Raise(changed);
    }

    /// <summary>Arms the listener; the clock runs only while activity is reported.</summary>
    public void StartAuto()
    {
        ClockState? changed;
        lock (_gate)
        {
            if (State is ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet) return;
            if (State == ClockState.ManualRunning) _committed = ElapsedUnlocked();
            _candidateStart = null;
            changed = SetState(ClockState.Listening);
        }
        Raise(changed);
    }

    public void Pause()
    {
        ClockState? changed;
        lock (_gate)
        {
            if (State is ClockState.Stopped or ClockState.ManualPaused) return;
            if (State == ClockState.Playing) CommitAutoRun();
            else _committed = ElapsedUnlocked();
            _candidateStart = null;
            changed = SetState(ClockState.ManualPaused);
        }
        Raise(changed);
    }

    /// <summary>
    /// Ends the current auto run at its last active moment and forgets any pending start. Call when the audio
    /// timeline is about to restart or stop being reported (new pipeline, learning, calibration) so the gap
    /// is never credited as practice.
    /// </summary>
    public void Interrupt()
    {
        ClockState? changed;
        lock (_gate) changed = InterruptUnlocked();
        Raise(changed);
    }

    private ClockState? InterruptUnlocked()
    {
        _candidateStart = null;
        if (State != ClockState.Playing) return null;
        CommitAutoRun();
        return SetState(ClockState.PausedQuiet);
    }

    /// <summary>Folds the auto run into the total, up to the last active sample (trailing quiet is not practice).</summary>
    private void CommitAutoRun()
    {
        if (State != ClockState.Playing) return;
        _committed += _lastActive - _runStart;
    }

    /// <summary>Feeds one activity sample; <paramref name="time"/> is the sample's own timestamp.</summary>
    public void OnActivity(TimeSpan time, bool active)
    {
        ClockState? changed = null;
        lock (_gate)
        {
            if (State is not (ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet)) return;
            // Time running backwards means the audio source restarted; the old run cannot continue.
            if (time < _lastSample) changed = InterruptUnlocked();
            _lastSample = time;
            if (State == ClockState.Playing)
            {
                if (active) _lastActive = time;
                else if (time - _lastActive >= StopDelay)
                {
                    CommitAutoRun();
                    _candidateStart = null;
                    changed = SetState(ClockState.PausedQuiet);
                }
            }
            else if (!active) _candidateStart = null;
            else
            {
                _candidateStart ??= time;
                if (time - _candidateStart.Value >= StartDelay)
                {
                    _runStart = _candidateStart.Value;
                    _lastActive = time;
                    changed = SetState(ClockState.Playing);
                }
            }
        }
        Raise(changed);
    }

    public void Reset()
    {
        ClockState? changed;
        lock (_gate)
        {
            _committed = TimeSpan.Zero;
            _goalFired = false;
            _candidateStart = null;
            changed = SetState(ClockState.Stopped);
        }
        Raise(changed);
    }

    /// <summary>Call periodically; raises GoalReached once when the goal is crossed.</summary>
    public void Tick()
    {
        bool fire;
        lock (_gate)
        {
            fire = _goal is { } g && !_goalFired && GoalElapsedUnlocked() >= g;
            if (fire) _goalFired = true;
        }
        if (fire) GoalReached?.Invoke();
    }

    /// <summary>Like Elapsed, but an auto run counts only to its last active sample, so the goal never fires on rolled-back time.</summary>
    private TimeSpan GoalElapsedUnlocked() =>
        State == ClockState.Playing ? _committed + (_lastActive - _runStart) : ElapsedUnlocked();

    private ClockState SetState(ClockState s)
    {
        State = s;
        return s;
    }

    /// <summary>Events fire outside the lock so a subscriber may call back into the clock.</summary>
    private void Raise(ClockState? changed)
    {
        if (changed is { } s) StateChanged?.Invoke(s);
    }
}
