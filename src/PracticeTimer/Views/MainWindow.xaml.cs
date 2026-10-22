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
        _vm.SettingsApplied += ApplyWindowSettings;
        _tray = new TrayIcon(Restore, Close);
        ApplyWindowSettings();
        Loaded += (_, _) =>
        {
            _vm.Start();
            if (_vm.Settings.StartMinimized) Hide();
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ElapsedText)) _tray.Tooltip = $"Practice Timer {_vm.ElapsedText}";
        };
    }

    private void ApplyWindowSettings()
    {
        var s = _vm.Settings;
        App.ApplyTheme(s.DarkTheme);
        Topmost = s.AlwaysOnTop;
        Root.LayoutTransform = new ScaleTransform(s.LargeText ? 1.25 : 1, s.LargeText ? 1.25 : 1);
        _tray.Visible = s.MinimizeToTray;
    }

    private bool? AskSave(TimeSpan elapsed)
    {
        var r = MessageBox.Show(this, $"Save this {elapsed:hh\\:mm\\:ss} session to history?", "Reset timer",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    private void Restore()
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
        if (!_vm.Reset()) { e.Cancel = true; return; }
        _vm.Dispose();
        _tray.Dispose();
    }

    private void StartPause_Click(object sender, RoutedEventArgs e) => _vm.StartPause();
    private void Reset_Click(object sender, RoutedEventArgs e) => _vm.Reset();
    private void Learn_Click(object sender, RoutedEventArgs e) => _vm.LearnMetronome();
    private void Forget_Click(object sender, RoutedEventArgs e) => _vm.ForgetAutoLearned();
    private void History_Click(object sender, RoutedEventArgs e) => new HistoryWindow(_vm.History) { Owner = this }.ShowDialog();
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_vm) { Owner = this }.ShowDialog();
}
