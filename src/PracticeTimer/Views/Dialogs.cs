using System.Windows;
using System.Windows.Controls;

namespace PracticeTimer.Views;

/// <summary>Small code-built dialogs. They pick up the app theme through the global window and control styles.</summary>
public static class Dialogs
{
    private static Window Shell(Window? owner, string title, UIElement content, double width = 380)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), Content = content,
        };
        w.SetResourceReference(Control.BackgroundProperty, "Bg");
        w.SetResourceReference(Control.ForegroundProperty, "Fg");
        if (owner is { IsVisible: true }) w.Owner = owner;
        return w;
    }

    private static StackPanel Buttons(Window w, string ok, Action? onOk = null)
    {
        var okButton = new Button { Content = ok, Width = 90, IsDefault = true, Style = (Style)Application.Current.FindResource("Primary") };
        okButton.Click += (_, _) => { onOk?.Invoke(); w.DialogResult = true; };
        var cancel = new Button { Content = "Skip", Width = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { okButton, cancel } };
    }

    /// <summary>Asks for a text value; null if skipped. An empty answer also counts as skipped.</summary>
    public static string? AskText(Window? owner, string title, string message, string initial)
    {
        var box = new TextBox { Text = initial, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, box } };
        var w = Shell(owner, title, panel);
        panel.Children.Add(Buttons(w, "Save"));
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    /// <summary>Lets the user pick one item; returns its index, or null if skipped.</summary>
    public static int? Pick(Window? owner, string title, string message, IReadOnlyList<string> items)
    {
        var list = new ListBox { ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 0), MaxHeight = 200 };
        var panel = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, list } };
        var w = Shell(owner, title, panel, 420);
        panel.Children.Add(Buttons(w, "Replace"));
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) w.DialogResult = true; };
        return w.ShowDialog() == true && list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
}
