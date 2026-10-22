using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Persistence;
using PracticeTimer.Core.Pipeline;
using PracticeTimer.Core.Sessions;
using PracticeTimer.Core.Time;
using PracticeTimer.Core.Timing;
using PracticeTimer.Platform;

namespace PracticeTimer.ViewModels;

public sealed class MainViewModel : ObservableBase, IDisposable
{
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PracticeTimer");

    private readonly SettingsStore _settingsStore = new(Path.Combine(DataDir, "settings.json"));
    private readonly MetronomeStore _metronomeStore = new(Path.Combine(DataDir, "metronomes.json"));
    private readonly MicCapture _capture = new();
    private readonly MicWatcher _watcher = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DetectorSettings _detector = new();
    private readonly SessionRecorder _recorder;
    private AudioPipeline? _pipeline;
    /// <summary>The microphone can't be used right now.</summary>
    private string? _micError;
    /// <summary>A microphone problem that already happened; stays visible until the user acts on the timer.</summary>
    private string? _micNotice;
    /// <summary>Short-lived message: learn results, save failures.</summary>
    private string _flash = "";
    private DateTime _flashUntil;
    private DateTime _checkUntil;
    private bool _isAuto;
    private bool _settingsDirty;
    private DateTime _settingsDueAt;
    private DateTime _todayAt = DateTime.MinValue;
    /// <summary>No audio for this long after data has been flowing means the device has stalled.</summary>
    private static readonly TimeSpan MicSilenceLimit = TimeSpan.FromSeconds(2.5);
    /// <summary>Some devices (a headset switching profile) take a while to deliver their first block.</summary>
    private static readonly TimeSpan MicFirstDataLimit = TimeSpan.FromSeconds(10);
    /// <summary>Calibrated floor per microphone, so reopening the same one doesn't lose it.</summary>
    private readonly Dictionary<string, NoiseCalibration> _noise = new();

    public MainViewModel()
    {
        Settings = _settingsStore.Load();
        Clock = new PracticeClock(new StopwatchClock());
        History = new HistoryStore(Path.Combine(DataDir, "history.json"));
        _recorder = new SessionRecorder(History, () => DateTime.Now);
        _recorder.Attach(Clock);
        Bank.Load(_metronomeStore.Load());
        Instruments = new ObservableCollection<string>(History.Instruments);
        History.SaveFailed += e => Flash("Couldn't save history: " + e.Message, 15);
        Clock.GoalReached += OnGoalReached; // raised from Tick, which runs on the UI thread
        _isAuto = Settings.Mode != "Manual";
        Instrument = Settings.LastInstrument ?? "";
        ApplyToRuntime(); // not saved: nothing has changed, and a file that failed to load must stay untouched
        _timer.Tick += (_, _) => Refresh();
        _watcher.Changed += () => Application.Current.Dispatcher.BeginInvoke(RefreshDevices);
        // NAudio raises this on the UI context already; MicCapture only raises it for the current capture, so no extra hop
        // that could let a stale event hit a newer capture.
        _capture.Stopped += ex => { if (Application.Current.Dispatcher.CheckAccess()) OnCaptureStopped(ex); else Application.Current.Dispatcher.BeginInvoke(() => OnCaptureStopped(ex)); };
    }

    public AppSettings Settings { get; }
    public PracticeClock Clock { get; }
    public HistoryStore History { get; }
    public FingerprintBank Bank { get; } = new();
    public ObservableCollection<MicDevice> Devices { get; } = new();

    /// <summary>Asks whether to save a session; true = save, false = discard, null = cancel.</summary>
    public Func<TimeSpan, bool?>? ConfirmSave { get; set; }
    /// <summary>Offers a default metronome name; returns the chosen name, or null to keep the default.</summary>
    public Func<string, string?>? PromptMetronomeName { get; set; }
    /// <summary>Called when all four slots are taken; returns the saved metronome to replace, or null to not save.</summary>
    public Func<IReadOnlyList<MetronomeProfile>, MetronomeProfile?>? ChooseReplacement { get; set; }
    public event Action? SettingsApplied;

    // ----- bound state -----
    private string _elapsedText = "00:00:00", _statusText = "Stopped", _tempoText = "", _metronomeText = "";
    private string _todayText = "", _goalText = "";
    private double _level, _threshold, _floor, _goalProgress;
    private FrameTag _tag;
    private bool _running, _learning, _calibrating;
    private MicDevice? _selectedDevice;
    private string _instrument = "";

    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string TempoText { get => _tempoText; private set => Set(ref _tempoText, value); }
    public string MetronomeText { get => _metronomeText; private set => Set(ref _metronomeText, value); }
    public string TodayText { get => _todayText; private set => Set(ref _todayText, value); }
    public string GoalText { get => _goalText; private set => Set(ref _goalText, value); }
    public double Level { get => _level; private set => Set(ref _level, value); }
    public double Threshold { get => _threshold; private set => Set(ref _threshold, value); }
    public double Floor { get => _floor; private set => Set(ref _floor, value); }
    public double GoalProgress { get => _goalProgress; private set => Set(ref _goalProgress, value); }
    public FrameTag Tag { get => _tag; private set => Set(ref _tag, value); }
    public bool IsRunning { get => _running; private set { if (Set(ref _running, value)) Raise(nameof(StartPauseLabel)); } }
    public bool IsLearning { get => _learning; private set { if (Set(ref _learning, value)) Raise(nameof(LearnLabel)); } }
    public bool IsCalibrating { get => _calibrating; private set => Set(ref _calibrating, value); }
    private double _learnProgress;
    private bool _learnCheck, _canLearn;
    public double LearnProgress { get => _learnProgress; private set => Set(ref _learnProgress, value); }
    public bool LearnCheck { get => _learnCheck; private set => Set(ref _learnCheck, value); }
    public bool CanLearn { get => _canLearn; private set => Set(ref _canLearn, value); }
    public string LearnLabel => IsLearning ? "Cancel learning" : "Learn metronome";
    public string StartPauseLabel => IsRunning ? "Pause" : "Start";
    public string Instrument { get => _instrument; set => Set(ref _instrument, value); }
    private string _note = "";
    public string Note { get => _note; set => Set(ref _note, value); }

    public int GoalMinutes
    {
        get => Settings.GoalMinutes;
        set
        {
            value = Math.Clamp(value, 0, 24 * 60);
            if (value != Settings.GoalMinutes)
            {
                Settings.GoalMinutes = value;
                Clock.Goal = value > 0 ? TimeSpan.FromMinutes(value) : null;
                SaveSettingsSoon();
            }
            Raise(); // also re-shows the clamped value if the typed one was out of range
        }
    }
    public ObservableCollection<string> Instruments { get; }

    public bool IsAuto
    {
        get => _isAuto;
        set
        {
            if (!Set(ref _isAuto, value)) return;
            Settings.Mode = value ? "Auto" : "Manual";
            if (IsRunning) Clock.Pause();
            SaveSettingsNow();
        }
    }

    public MicDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            // The combo box pushes null whenever the device list is rebuilt; that is not a choice.
            if (value == null || !Set(ref _selectedDevice, value)) return;
            Settings.DeviceId = value.Id;
            _micNotice = null;
            SaveSettingsNow();
            StartCapture();
        }
    }

    // ----- lifecycle -----
    public void Start()
    {
        RefreshDevices();
        _timer.Start();
    }

    public void RefreshDevices()
    {
        var previous = _selectedDevice;
        List<MicDevice> list;
        try { list = MicCapture.List(); } catch (Exception ex) { App.Log(ex); list = new(); }
        // Diff rather than rebuild: clearing the list makes the combo box drop its selection.
        foreach (var gone in Devices.Where(d => list.All(n => n.Id != d.Id)).ToList()) Devices.Remove(gone);
        foreach (var added in list.Where(n => Devices.All(d => d.Id != n.Id))) Devices.Add(added);
        MicDevice? Find(string? id) => id == null ? null : Devices.FirstOrDefault(d => d.Id == id);

        var lost = previous != null && Find(previous.Id) == null;
        if (lost && _pipeline != null) HandleMicLost($"{previous!.Name} was disconnected.");

        // The remembered microphone wins whenever it is present, so the app returns to it after a fallback.
        var pick = Find(Settings.DeviceId) ?? Find(previous?.Id) ?? Find(MicCapture.DefaultId()) ?? Devices.FirstOrDefault();
        if (pick == null)
        {
            _capture.Stop();
            _pipeline = null;
            _micError = "No microphone found";
            return;
        }
        if (pick.Id != previous?.Id || _pipeline == null)
        {
            _selectedDevice = pick;
            Raise(nameof(SelectedDevice));
            StartCapture();
            if (_pipeline != null && _micNotice != null)
            {
                // Back on the remembered microphone the old warning no longer applies; on a fallback it says where we are.
                _micNotice = pick.Id == Settings.DeviceId ? null : lost ? _micNotice + $" Now listening with {pick.Name}. Press Start to continue." : _micNotice;
            }
        }
    }

    private void StartCapture()
    {
        // Each pipeline numbers its audio from zero, so end any auto run before the old timeline goes away.
        Clock.Interrupt();
        RememberCalibration();
        _pipeline = null;
        try
        {
            var id = _selectedDevice!.Id;
            if (IsLearning) Flash("Learning stopped because the microphone changed.", 8);
            AudioPipeline? pipeline = null;
            var rate = _capture.Start(id, samples => pipeline?.Push(samples));
            _noiseMicId = id;
            pipeline = new AudioPipeline(rate, Clock, _detector, Bank, _noise.TryGetValue(id, out var cal) ? cal : null)
            { AutoLearnEnabled = Settings.AutoLearnMetronome };
            var mine = pipeline;
            // A learn finishing on a pipeline that has since been replaced belongs to the old microphone.
            pipeline.LearnFinished += s => Application.Current.Dispatcher.BeginInvoke(() => { if (ReferenceEquals(mine, _pipeline)) OnLearnFinished(s); });
            _pipeline = pipeline;
            _micError = null;
        }
        catch (Exception ex)
        {
            _micError = "Can't open microphone: " + ex.Message;
        }
    }

    /// <summary>Pauses the timer, drops the dead pipeline and leaves a warning that stays until the user acts.</summary>
    private void HandleMicLost(string reason)
    {
        RememberCalibration();
        // Only auto mode depends on the microphone; a manual stopwatch keeps running.
        var auto = Clock.State is ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet;
        Clock.Interrupt();
        if (auto) Clock.Pause();
        _capture.Stop();
        _pipeline = null;
        _micNotice = auto ? reason + " The timer is paused." : reason;
    }

    private void OnCaptureStopped(Exception? ex)
    {
        if (_pipeline == null) return;
        if (ex != null) App.Log(ex); // the audio thread's own failures never reach the unhandled-exception handler
        HandleMicLost(ex == null ? "The microphone stopped." : "Microphone lost.");
        RefreshDevices();
    }

    /// <summary>Called when the computer is about to sleep: audio and wall-clock time are unreliable across it.</summary>
    public void OnSuspend()
    {
        if (Clock.State is not (ClockState.Stopped or ClockState.ManualPaused))
        {
            Clock.Interrupt();
            Clock.Pause();
            _micNotice = "The timer paused when the computer went to sleep. Press Start to continue.";
        }
    }

    /// <summary>Called after resume; the capture device often needs reopening.</summary>
    public void OnResume() => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        RefreshDevices();
        if (_selectedDevice != null && _pipeline != null) StartCapture(); // the old stream is usually dead after sleep
    });

    /// <summary>Keeps the pipeline's calibrated floor for the current microphone, for when it is reopened.</summary>
    private void RememberCalibration()
    {
        if (_pipeline?.Calibration is { } cal && _noiseMicId != null) _noise[_noiseMicId] = cal;
    }

    private string? _noiseMicId;

    /// <summary>Pushes settings into the clock and detector.</summary>
    private void ApplyToRuntime()
    {
        _detector.MarginDb = Settings.SensitivityMarginDb;
        _detector.AbsoluteMinDb = Settings.MinTriggerDb;
        _detector.DutyThreshold = Settings.DutyThreshold;
        _detector.MetronomeFilterEnabled = Settings.MetronomeFilterEnabled;
        Clock.StartDelay = TimeSpan.FromSeconds(Settings.StartDelaySeconds);
        Clock.StopDelay = TimeSpan.FromSeconds(Settings.StopDelaySeconds);
        Clock.Goal = Settings.GoalMinutes > 0 ? TimeSpan.FromMinutes(Settings.GoalMinutes) : null;
        if (_pipeline != null) _pipeline.AutoLearnEnabled = Settings.AutoLearnMetronome;
        if (!Settings.MetronomeFilterEnabled) _pipeline?.CancelLearn(); // learning needs the filter
    }

    /// <summary>Applies and saves settings after the user pressed OK in the settings window.</summary>
    public void ApplySettings()
    {
        ApplyToRuntime();
        SaveSettingsNow();
        Raise(nameof(GoalMinutes));
        SettingsApplied?.Invoke();
    }

    // ----- commands -----
    public void StartPause()
    {
        _micNotice = null;
        if (IsRunning) { Clock.Pause(); return; }
        if (_pipeline == null) RefreshDevices(); // retry a microphone that was lost or wouldn't open
        if (IsLearning) _pipeline?.CancelLearn();
        if (IsAuto) Clock.StartAuto(); else Clock.Start();
    }

    /// <summary>Returns false if the user cancelled.</summary>
    public bool Reset()
    {
        Clock.Interrupt(); // what is saved and shown is the credited time, without trailing quiet
        if (Clock.Elapsed.TotalSeconds >= 3)
        {
            bool? answer = ConfirmSave != null ? ConfirmSave(Clock.Elapsed) : false;
            if (answer == null) return false;
            if (answer == true) SaveSession();
        }
        Clock.Reset();
        Note = "";
        return true;
    }

    private void SaveSession()
    {
        Settings.LastInstrument = string.IsNullOrWhiteSpace(Instrument) ? null : Instrument.Trim();
        SaveSettingsNow();
        _recorder.Save(Clock, Instrument, Note);
        // Insert only what is new: rebuilding the list would clear the editable instrument box.
        foreach (var i in History.Instruments.Reverse())
            if (!Instruments.Contains(i, StringComparer.OrdinalIgnoreCase)) Instruments.Insert(0, i);
    }

    /// <summary>Saves a session worth keeping and writes pending settings. For window close and logoff, where asking isn't possible.</summary>
    public void SaveOnExit()
    {
        Clock.Interrupt();
        if (Clock.Elapsed.TotalSeconds >= 3)
        {
            SaveSession();
            Clock.Reset(); // saved: if a cancelled shutdown leaves the app open, a later close must not save it again
            Note = "";
        }
        FlushSettings();
    }

    public void LearnMetronome()
    {
        if (_pipeline == null || !CanLearn) return;
        _micNotice = null;
        if (IsLearning) { _pipeline.CancelLearn(); return; }
        _pipeline.StartLearn();
    }

    /// <summary>Moves the trigger level to a meter position (0..1), as dragged on the level meter.</summary>
    public void SetThresholdLevel(double level)
    {
        var floorDb = (_pipeline?.Snapshot ?? MeterSnapshot.Empty).FloorDb;
        var (margin, min) = LoudnessGate.SettingsFor(level * 80 - 80, floorDb);
        Settings.SensitivityMarginDb = margin;
        Settings.MinTriggerDb = min;
        _detector.MarginDb = margin;
        _detector.AbsoluteMinDb = min;
        SaveSettingsSoon(); // fires on every mouse move while dragging
    }

    public void Calibrate()
    {
        _micNotice = null;
        _pipeline?.StartCalibration();
    }

    public void ForgetAutoLearned()
    {
        Bank.ClearTentative(); // tentative profiles are never saved, so there is nothing to write
    }

    public void SaveMetronomes() => TrySave(() => _metronomeStore.Save(Bank.Profiles), "metronomes");

    private void OnLearnFinished(GuidedLearnSession s)
    {
        switch (s.Status)
        {
            case LearnStatus.Success when s.Result != null:
                KeepLearned(s.Result.Id, s.Bpm);
                break;
            case LearnStatus.BankFull when s.Result != null:
                var victim = ChooseReplacement?.Invoke(Bank.Profiles.Where(p => p.Locked).ToList());
                if (victim != null)
                {
                    Bank.Remove(victim.Id);
                    var add = Bank.TryAdd(s.Result);
                    if (add.Kind != AddKind.Full) { KeepLearned(add.Profile.Id, s.Bpm); break; }
                }
                Flash("Four metronomes are already saved, so this one wasn't kept. Delete one in Settings first.", 10);
                break;
            case LearnStatus.AlreadyKnown:
                SaveMetronomes();
                System.Media.SystemSounds.Asterisk.Play();
                Flash($"Already known: {s.Result?.Name}", 8);
                break;
            case LearnStatus.Timeout: Flash("Didn't hear a steady metronome. Try again.", 8); break;
            case LearnStatus.TooMuchSound: Flash("Too much other sound. Try again.", 8); break;
        }
    }

    /// <summary>Names, stamps, saves and announces a freshly learned metronome.</summary>
    private void KeepLearned(string id, double? bpm)
    {
        var name = NextMetronomeName();
        name = PromptMetronomeName?.Invoke(name) ?? name;
        Bank.ApplyEdits(new[] { new ProfileEdit(id, name, true, false) });
        Bank.SetMic(id, SelectedDevice?.Id, SelectedDevice?.Name);
        SaveMetronomes();
        System.Media.SystemSounds.Asterisk.Play();
        Flash($"Metronome learned (BPM ~{bpm:0})", 8);
        _checkUntil = DateTime.Now.AddSeconds(8);
    }

    /// <summary>"Metronome N" with the lowest N not already used by a saved metronome.</summary>
    private string NextMetronomeName()
    {
        var used = Bank.Profiles.Where(p => p.Locked).Select(p => p.Name).ToHashSet();
        var n = 1;
        while (used.Contains($"Metronome {n}")) n++;
        return $"Metronome {n}";
    }

    private void Flash(string text, double seconds)
    {
        _flash = text;
        _flashUntil = DateTime.Now.AddSeconds(seconds);
    }

    private bool TrySave(Action save, string what)
    {
        try { save(); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Couldn't save {what}: {e.Message}", 15);
            return false;
        }
    }

    private void SaveSettingsNow()
    {
        _settingsDirty = false;
        TrySave(() => _settingsStore.Save(Settings), "settings");
    }

    /// <summary>For values that change continuously (a drag, typing): written shortly after the last change.</summary>
    private void SaveSettingsSoon()
    {
        _settingsDirty = true;
        _settingsDueAt = DateTime.Now.AddSeconds(1);
    }

    private void FlushSettings()
    {
        if (_settingsDirty) SaveSettingsNow();
    }

    private void OnGoalReached()
    {
        if (Settings.ChimeEnabled) System.Media.SystemSounds.Exclamation.Play();
    }

    // ----- periodic refresh -----
    private void Refresh()
    {
        Clock.Tick();
        if (_settingsDirty && DateTime.Now >= _settingsDueAt) FlushSettings();
        if (_pipeline != null && _capture.IsRunning && _capture.SilenceAge > (_capture.HasData ? MicSilenceLimit : MicFirstDataLimit))
        {
            HandleMicLost("The microphone stopped sending audio.");
            _micError = "Microphone not responding. Press Start to retry.";
        }
        var e = Clock.Elapsed;
        ElapsedText = $"{(int)e.TotalHours:00}:{e.Minutes:00}:{e.Seconds:00}";
        IsRunning = Clock.State is ClockState.ManualRunning or ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet;

        var snap = _pipeline?.TakeSnapshot() ?? MeterSnapshot.Empty;
        var learn = _pipeline?.Learn;
        IsLearning = learn is { IsDone: false };
        IsCalibrating = snap.Calibrating;
        LearnProgress = learn is { IsDone: false } ? Math.Min(1.0, (double)learn.TickCount / GuidedLearnSession.RequiredTicks) : 0;
        LearnCheck = !IsLearning && DateTime.Now < _checkUntil;
        CanLearn = IsLearning || (_pipeline != null && Settings.MetronomeFilterEnabled && !IsCalibrating); // cancelling is always possible
        Level = Scale(snap.Dbfs);
        Threshold = Scale(snap.ThresholdDb);
        Floor = Scale(snap.FloorDb);
        Tag = snap.Tag;
        TempoText = snap.Bpm is { } bpm ? $"Tempo ~{bpm:0} BPM" : "";

        var flash = DateTime.Now < _flashUntil && _flash != "" ? _flash : null;
        StatusText = _micError ?? _micNotice
            ?? (IsCalibrating ? "Calibrating... stay quiet for a few seconds"
            : IsLearning ? $"Learning metronome... {learn!.TickCount}/{GuidedLearnSession.RequiredTicks} ticks. Let it run on its own."
            : flash
            ?? Clock.State switch
            {
                ClockState.Stopped => "Stopped",
                ClockState.ManualRunning => "Running",
                ClockState.ManualPaused => "Paused",
                ClockState.Listening => "Listening... waiting for sound",
                ClockState.Playing => "Playing",
                _ => "Paused (quiet)",
            });

        var (saved, tentative, other) = Bank.Summary(SelectedDevice?.Id);
        MetronomeText = saved + tentative == 0 ? "No metronome learned"
            : $"Metronomes: {saved} saved" + (tentative > 0 ? " + Metronome (auto)" : "") + (other > 0 ? $" ({other} learned on another mic)" : "");

        if (Clock.Goal is { } goal)
        {
            var credited = Clock.CreditedElapsed; // trailing quiet may still be rolled back, so don't announce on it
            GoalProgress = Math.Min(1, credited.TotalSeconds / goal.TotalSeconds);
            GoalText = credited >= goal ? "Goal reached!" : $"{(goal - credited).TotalMinutes:0.#} min to go";
        }
        else { GoalProgress = 0; GoalText = "No goal set"; }

        if (DateTime.Now - _todayAt >= TimeSpan.FromSeconds(1))
        {
            _todayAt = DateTime.Now;
            var today = HistoryAggregator.Today(History.Sessions, DateTime.Now);
            TodayText = $"Saved today: {(int)today.TotalHours:00}:{today.Minutes:00}:{today.Seconds:00}";
        }
    }

    private static double Scale(double db) => Math.Clamp((db + 80) / 80, 0, 1);

    public void Dispose()
    {
        _timer.Stop();
        FlushSettings();
        _capture.Dispose();
        _watcher.Dispose();
    }
}
