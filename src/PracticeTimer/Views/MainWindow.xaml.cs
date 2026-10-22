using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using PracticeTimer.Platform;
using PracticeTimer.ViewModels;

namespace PracticeTimer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly TrayIcon _tray;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.ConfirmSave = AskSave;
        _vm.PromptMetronomeName = name => Dialogs.AskText(this, "Name this metronome", "Learned! Give it a name, or skip to keep the default.", name);
        _vm.ChooseReplacement = saved =>
        {
            var pick = Dialogs.Pick(this, "Metronomes full",
                "Four metronomes are already saved. Choose one to replace with the one you just taught, or skip to discard it.",
                saved.Select(p => p.Name).ToList());
            return pick is { } i ? saved[i] : null;
        };
        Meter.ThresholdDragged += level => _vm.SetThresholdLevel(level);
        _vm.SettingsApplied += ApplyWindowSettings;
        _tray = new TrayIcon(Restore, Close);
        ApplyWindowSettings();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Application.Current.SessionEnding += OnSessionEnding;
        // Not from Loaded: that event waits for the dispatcher to go idle, and the timer must not depend on it.
        _vm.Start();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ElapsedText)) _tray.Tooltip = $"Practice Timer {_vm.ElapsedText}";
        };
    }

    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && _vm.Settings.Theme == "System")
            Dispatcher.BeginInvoke(() => App.ApplyTheme("System"));
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Dispatcher.Invoke(_vm.OnSuspend);
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume) _vm.OnResume();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) => _vm.SaveOnExit();

    private void ApplyWindowSettings()
    {
        var s = _vm.Settings;
        App.ApplyTheme(s.Theme);
        Topmost = s.AlwaysOnTop;
        var scale = s.LargeText ? 1.25 : 1;
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        // The content is scaled, so the window's minimum size must grow with it or the layout clips.
        MinWidth = 460 * scale;
        MinHeight = 560 * scale;
        Width = Math.Max(Width, MinWidth);
        Height = Math.Max(Height, MinHeight);
        _tray.Visible = s.MinimizeToTray;
        // If the tray icon went away while the window is hidden, nothing could bring it back.
        if (!s.MinimizeToTray && !IsVisible) Restore();
    }

    private bool? AskSave(TimeSpan elapsed)
    {
        if (!IsVisible) Restore(); // a prompt owned by a hidden window is easy to miss
        var r = MessageBox.Show(this, $"Save this {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00} session to history?", "Reset timer",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    /// <summary>Called once the window is shown, for the "start minimised" setting.</summary>
    public void ApplyStartMinimized()
    {
        if (!_vm.Settings.StartMinimized) return;
        // Hiding is only safe when the tray icon is there to bring the window back.
        if (_vm.Settings.MinimizeToTray) Hide();
        else WindowState = WindowState.Minimized;
    }

    public void Restore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _vm.Settings.MinimizeToTray) Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _vm.SaveOnExit(); // closing keeps the session; Reset is where the user chooses to discard
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Application.Current.SessionEnding -= OnSessionEnding;
        _vm.Dispose();
        _tray?.Dispose();
    }

    private void StartPause_Click(object sender, RoutedEventArgs e) => _vm.StartPause();
    private void Reset_Click(object sender, RoutedEventArgs e) => _vm.Reset();
    private void Learn_Click(object sender, RoutedEventArgs e) => _vm.LearnMetronome();
    private void Forget_Click(object sender, RoutedEventArgs e) => _vm.ForgetAutoLearned();
    private void History_Click(object sender, RoutedEventArgs e) => new HistoryWindow(_vm.History) { Owner = this }.ShowDialog();
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_vm) { Owner = this }.ShowDialog();
}
