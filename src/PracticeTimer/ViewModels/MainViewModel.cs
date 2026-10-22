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
    private string? _micError;
    private string _learnMessage = "";
    private DateTime _learnMessageUntil;
    private bool _isAuto;
    private bool _goalAnnounced;

    public MainViewModel()
    {
        Settings = _settingsStore.Load();
        Clock = new PracticeClock(new StopwatchClock());
        History = new HistoryStore(Path.Combine(DataDir, "history.json"));
        _recorder = new SessionRecorder(History, () => DateTime.Now);
        _recorder.Attach(Clock);
        Bank.Load(_metronomeStore.Load());
        Clock.GoalReached += () => Application.Current.Dispatcher.BeginInvoke(OnGoalReached);
        _isAuto = Settings.Mode != "Manual";
        Instrument = Settings.LastInstrument ?? "";
        ApplySettings();
        _timer.Tick += (_, _) => Refresh();
        _watcher.Changed += () => Application.Current.Dispatcher.BeginInvoke(RefreshDevices);
        _capture.Stopped += ex => Application.Current.Dispatcher.BeginInvoke(() => OnCaptureStopped(ex));
    }

    public AppSettings Settings { get; }
    public PracticeClock Clock { get; }
    public HistoryStore History { get; }
    public FingerprintBank Bank { get; } = new();
    public ObservableCollection<MicDevice> Devices { get; } = new();

    /// <summary>Asks whether to save a session; true = save, false = discard, null = cancel.</summary>
    public Func<TimeSpan, bool?>? ConfirmSave { get; set; }
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
    public bool IsLearning { get => _learning; private set => Set(ref _learning, value); }
    public bool IsCalibrating { get => _calibrating; private set => Set(ref _calibrating, value); }
    public string StartPauseLabel => IsRunning ? "Pause" : "Start";
    public string Instrument { get => _instrument; set => Set(ref _instrument, value); }
    public string Note { get; set; } = "";

    public int GoalMinutes
    {
        get => Settings.GoalMinutes;
        set
        {
            value = Math.Clamp(value, 0, 24 * 60);
            if (value == Settings.GoalMinutes) return;
            Settings.GoalMinutes = value;
            Clock.Goal = value > 0 ? TimeSpan.FromMinutes(value) : null;
            _goalAnnounced = false;
            _settingsStore.Save(Settings);
            Raise();
        }
    }
    public IReadOnlyList<string> Instruments => History.Instruments;

    public bool IsAuto
    {
        get => _isAuto;
        set
        {
            if (!Set(ref _isAuto, value)) return;
            Settings.Mode = value ? "Auto" : "Manual";
            if (IsRunning) Clock.Pause();
            _settingsStore.Save(Settings);
        }
    }

    public MicDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value) || value == null) return;
            Settings.DeviceId = value.Id;
            Settings.DeviceName = value.Name;
            _settingsStore.Save(Settings);
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
        var current = SelectedDevice?.Id ?? Settings.DeviceId;
        List<MicDevice> list;
        try { list = MicCapture.List(); } catch (Exception) { list = new(); }
        var same = list.Select(d => d.Id).SequenceEqual(Devices.Select(d => d.Id));
        if (!same)
        {
            Devices.Clear();
            foreach (var d in list) Devices.Add(d);
        }
        var pick = Devices.FirstOrDefault(d => d.Id == current)
                   ?? Devices.FirstOrDefault(d => d.Id == MicCapture.DefaultId())
                   ?? Devices.FirstOrDefault();
        if (pick?.Id != _selectedDevice?.Id || (_pipeline == null && pick != null))
        {
            _selectedDevice = pick;
            Raise(nameof(SelectedDevice));
            if (pick != null) StartCapture();
        }
        if (pick == null) { _capture.Stop(); _pipeline = null; _micError = "No microphone found"; }
    }

    private void StartCapture()
    {
        try
        {
            var id = _selectedDevice!.Id;
            AudioPipeline? pipeline = null;
            var rate = _capture.Start(id, (samples, r) => pipeline?.Push(samples));
            pipeline = new AudioPipeline(rate, Clock, _detector, Bank) { AutoLearnEnabled = Settings.AutoLearnMetronome };
            pipeline.LearnFinished += s => Application.Current.Dispatcher.BeginInvoke(() => OnLearnFinished(s));
            _pipeline = pipeline;
            _micError = null;
        }
        catch (Exception ex)
        {
            _pipeline = null;
            _micError = "Can't open microphone: " + ex.Message;
        }
    }

    private void OnCaptureStopped(Exception? ex)
    {
        if (ex == null) return;
        if (IsRunning) Clock.Pause();
        _pipeline = null;
        _micError = "Microphone lost. Pick a microphone to continue.";
        RefreshDevices();
    }

    public void ApplySettings()
    {
        _detector.MarginDb = Settings.SensitivityMarginDb;
        _detector.AbsoluteMinDb = Settings.MinTriggerDb;
        _detector.DutyThreshold = Settings.DutyThreshold;
        _detector.MetronomeFilterEnabled = Settings.MetronomeFilterEnabled;
        Clock.StartDelay = TimeSpan.FromSeconds(Settings.StartDelaySeconds);
        Clock.StopDelay = TimeSpan.FromSeconds(Settings.StopDelaySeconds);
        Clock.Goal = Settings.GoalMinutes > 0 ? TimeSpan.FromMinutes(Settings.GoalMinutes) : null;
        if (_pipeline != null) _pipeline.AutoLearnEnabled = Settings.AutoLearnMetronome;
        _settingsStore.Save(Settings);
        Raise(nameof(GoalMinutes));
        SettingsApplied?.Invoke();
    }

    // ----- commands -----
    public void StartPause()
    {
        if (IsRunning) { Clock.Pause(); return; }
        if (IsLearning) _pipeline?.CancelLearn();
        if (IsAuto) Clock.StartAuto(); else Clock.Start();
    }

    /// <summary>Returns false if the user cancelled.</summary>
    public bool Reset()
    {
        if (Clock.Elapsed.TotalSeconds >= 3)
        {
            bool? answer = ConfirmSave != null ? ConfirmSave(Clock.Elapsed) : false;
            if (answer == null) return false;
            if (answer == true) SaveSession();
        }
        Clock.Reset();
        _goalAnnounced = false;
        Note = "";
        Raise(nameof(Note));
        return true;
    }

    private void SaveSession()
    {
        Settings.LastInstrument = string.IsNullOrWhiteSpace(Instrument) ? null : Instrument.Trim();
        _settingsStore.Save(Settings);
        _recorder.Save(Clock, Instrument, Note);
        Raise(nameof(Instruments));
    }

    public void LearnMetronome()
    {
        if (_pipeline == null) return;
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
        _settingsStore.Save(Settings);
    }

    public void Calibrate() => _pipeline?.StartCalibration();

    public void ForgetAutoLearned()
    {
        Bank.ClearTentative();
        SaveMetronomes();
    }

    public void SaveMetronomes() => _metronomeStore.Save(Bank.Profiles);

    private void OnLearnFinished(GuidedLearnSession s)
    {
        var message = s.Status switch
        {
            LearnStatus.Success => $"Metronome learned (BPM ~{s.Bpm:0})",
            LearnStatus.AlreadyKnown => $"Already known: {s.Result?.Name}",
            LearnStatus.Timeout => "Didn't hear a steady metronome. Try again.",
            LearnStatus.TooMuchSound => "Too much other sound. Try again.",
            LearnStatus.BankFull => "Four metronomes are already saved. Delete one in Settings first.",
            _ => "",
        };
        if (s.Status is LearnStatus.Success && s.Result != null)
        {
            Bank.ApplyEdits(new[] { new ProfileEdit(s.Result.Id, NextMetronomeName(), true, false) });
            Bank.SetMic(s.Result.Id, SelectedDevice?.Id, SelectedDevice?.Name);
            SaveMetronomes();
        }
        if (s.Status == LearnStatus.AlreadyKnown) SaveMetronomes();
        if (s.Status is LearnStatus.Success or LearnStatus.AlreadyKnown) System.Media.SystemSounds.Asterisk.Play();
        _learnMessage = message;
        _learnMessageUntil = DateTime.Now.AddSeconds(8);
    }

    /// <summary>"Metronome N" with the lowest N not already used by a saved metronome.</summary>
    private string NextMetronomeName()
    {
        var used = Bank.Profiles.Where(p => p.Locked).Select(p => p.Name).ToHashSet();
        var n = 1;
        while (used.Contains($"Metronome {n}")) n++;
        return $"Metronome {n}";
    }

    private void OnGoalReached()
    {
        if (_goalAnnounced) return;
        _goalAnnounced = true;
        if (Settings.ChimeEnabled) System.Media.SystemSounds.Exclamation.Play();
    }

    // ----- periodic refresh -----
    private void Refresh()
    {
        Clock.Tick();
        var e = Clock.Elapsed;
        ElapsedText = $"{(int)e.TotalHours:00}:{e.Minutes:00}:{e.Seconds:00}";
        IsRunning = Clock.State is ClockState.ManualRunning or ClockState.Listening or ClockState.Playing or ClockState.PausedQuiet;

        var snap = _pipeline?.TakeSnapshot() ?? MeterSnapshot.Empty;
        var learn = _pipeline?.Learn;
        IsLearning = learn is { IsDone: false };
        IsCalibrating = snap.Calibrating;
        Level = Scale(snap.Dbfs);
        Threshold = Scale(snap.ThresholdDb);
        Floor = Scale(snap.FloorDb);
        Tag = snap.Tag;
        TempoText = snap.Bpm is { } bpm ? $"Tempo ~{bpm:0} BPM" : "";

        StatusText = _micError
            ?? (IsCalibrating ? "Calibrating... stay quiet for a few seconds"
            : IsLearning ? $"Learning metronome... {learn!.TickCount}/{GuidedLearnSession.RequiredTicks} ticks. Let it run on its own."
            : DateTime.Now < _learnMessageUntil && _learnMessage != "" ? _learnMessage
            : Clock.State switch
            {
                ClockState.Stopped => "Stopped",
                ClockState.ManualRunning => "Running",
                ClockState.ManualPaused => "Paused",
                ClockState.Listening => "Listening... waiting for sound",
                ClockState.Playing => "Playing",
                _ => "Paused (quiet)",
            });

        var saved = Bank.Profiles.Count(p => p.Locked);
        var other = Bank.Profiles.Count(p => p.Locked && MetronomeStore.LearnedOnDifferentMic(p, SelectedDevice?.Id));
        MetronomeText = Bank.Profiles.Count == 0 ? "No metronome learned"
            : $"Metronomes: {saved} saved" + (Bank.Profiles.Count > saved ? " + auto" : "") + (other > 0 ? $" ({other} learned on another mic)" : "");

        if (Clock.Goal is { } goal)
        {
            GoalProgress = Math.Min(1, e.TotalSeconds / goal.TotalSeconds);
            GoalText = e >= goal ? "Goal reached!" : $"{(goal - e).TotalMinutes:0.#} min to go";
        }
        else { GoalProgress = 0; GoalText = "No goal set"; }

        var today = HistoryAggregator.Today(History.Sessions, DateTime.Now);
        TodayText = $"Saved today: {(int)today.TotalHours:00}:{today.Minutes:00}:{today.Seconds:00}";
    }

    private static double Scale(double db) => Math.Clamp((db + 80) / 80, 0, 1);

    public void Dispose()
    {
        _timer.Stop();
        _capture.Dispose();
        _watcher.Dispose();
    }
}
