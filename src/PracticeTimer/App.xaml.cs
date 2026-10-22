using System.Windows;
using System.Windows.Media;

namespace PracticeTimer;

public partial class App : Application
{
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
