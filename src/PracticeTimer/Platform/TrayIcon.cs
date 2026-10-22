using System.Windows;

namespace PracticeTimer.Platform;

public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayIcon(Action restore, Action exit)
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Practice Timer",
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Items.Add("Show", null, (_, _) => restore());
        _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => exit());
        _icon.DoubleClick += (_, _) => restore();
    }

    public bool Visible { get => _icon.Visible; set => _icon.Visible = value; }

    public string Tooltip { set => _icon.Text = value.Length > 63 ? value[..63] : value; }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
