using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PracticeTimer;

public partial class App : Application
{
    private const string InstanceName = @"Local\PracticeTimer.Instance";
    private const string ShowSignalName = @"Local\PracticeTimer.Show";
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private bool _crashShown;

    /// <summary>
    /// One instance only: two would each hold their own copy of history.json and overwrite each other's sessions.
    /// A second launch asks the first to show its window, then exits.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, InstanceName, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject as Exception);
        var waiter = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => (MainWindow as Views.MainWindow)?.Restore());
        }) { IsBackground = true, Name = "show-signal" };
        waiter.Start();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        try { _instance?.ReleaseMutex(); } catch (ApplicationException) { }
        _instance?.Dispose();
    }

    /// <summary>A bug must not take a running practice session down with it: log, tell the user once, carry on.</summary>
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        if (_crashShown) return; // a repeating fault must not stack up message boxes
        _crashShown = true;
        MessageBox.Show($"Something went wrong, but the timer is still running.\n\n{e.Exception.Message}\n\nDetails were written to {LogPath}",
            "Practice Timer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string LogPath => Path.Combine(ViewModels.MainViewModel.DataDir, "error.log");

    private static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(ViewModels.MainViewModel.DataDir);
            File.AppendAllText(LogPath, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (Exception) { /* nothing more can be done */ }
    }

    /// <summary>Follows the Windows app-mode setting when <paramref name="theme"/> is "System".</summary>
    public static bool IsDark(string theme) => theme switch
    {
        "Dark" => true,
        "Light" => false,
        _ => SystemPrefersDark(),
    };

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception) { return false; }
    }

    public static void ApplyTheme(string theme)
    {
        var dark = IsDark(theme);
        var r = Current.Resources;
        static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        r["Bg"] = B(dark ? "#14171C" : "#F6F7F9");
        r["Panel"] = B(dark ? "#1F242C" : "#FFFFFF");
        r["Fg"] = B(dark ? "#ECEFF4" : "#1B1F24");
        r["Muted"] = B(dark ? "#98A2B3" : "#667085");
        r["Accent"] = B(dark ? "#4C8DFF" : "#2563EB");
        r["Border"] = B(dark ? "#3A424E" : "#D0D5DD");
    }
}
