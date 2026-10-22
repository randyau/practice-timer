using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        try { File.Delete(TracePath); } catch (Exception) { }
        Trace("startup");
        base.OnStartup(e);
        try
        {
            MainWindow = new Views.MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            // Without this the process would live on with no window and block every later launch.
            Fail(ex);
        }
    }

    /// <summary>The app can't start: say why, then exit so the next launch isn't blocked by a windowless process.</summary>
    private void Fail(Exception ex)
    {
        Log(ex);
        MessageBox.Show($"Practice Timer couldn't start.\n\n{ex.Message}\n\nDetails were written to {LogPath}", "Practice Timer", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
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
        e.Handled = true;
        if (MainWindow is not { IsLoaded: true }) { Fail(e.Exception); return; }
        Log(e.Exception);
        if (_crashShown) return; // a repeating fault must not stack up message boxes
        _crashShown = true;
        MessageBox.Show($"Something went wrong, but the timer is still running.\n\n{e.Exception.Message}\n\nDetails were written to {LogPath}",
            "Practice Timer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string LogPath => Path.Combine(ViewModels.MainViewModel.DataDir, "error.log");

    private static readonly string TracePath = Path.Combine(ViewModels.MainViewModel.DataDir, "trace.log");

    /// <summary>A short record of startup, rewritten each run, so a window that comes up dead can be diagnosed.</summary>
    internal static void Trace(string message)
    {
        try
        {
            Directory.CreateDirectory(ViewModels.MainViewModel.DataDir);
            File.AppendAllText(TracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (Exception) { /* diagnostics only */ }
    }

    internal static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(ViewModels.MainViewModel.DataDir);
            File.AppendAllText(LogPath, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (Exception) { /* nothing more can be done */ }
    }

    /// <summary>Follows the Windows app-mode setting when <paramref name="theme"/> is "System".</summary>
    private static bool IsDark(string theme) => theme switch
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
        // Brushes defined in XAML are frozen in a published build, so they are replaced, not recoloured.
        // Every user of them binds with DynamicResource and picks up the new one.
        void Set(string key, string light, string darkHex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkHex : light));
            brush.Freeze();
            r[key] = brush;
        }
        Set("Bg", "#F6F7F9", "#14171C");
        Set("Panel", "#FFFFFF", "#1F242C");
        Set("Fg", "#1B1F24", "#ECEFF4");
        Set("Muted", "#667085", "#98A2B3");
        Set("Accent", "#2563EB", "#4C8DFF");
        Set("Border", "#D0D5DD", "#3A424E");
        Set("Hover", "#E8EEFB", "#2B3340");
        // Re-bind the windows explicitly so none can keep a brush from before the switch.
        foreach (Window w in Current.Windows)
        {
            w.SetResourceReference(Control.BackgroundProperty, "Bg");
            w.SetResourceReference(Control.ForegroundProperty, "Fg");
        }
    }
}
