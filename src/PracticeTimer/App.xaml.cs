using System.Windows;
using System.Windows.Media;

namespace PracticeTimer;

public partial class App : Application
{
    public static void ApplyTheme(bool dark)
    {
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
