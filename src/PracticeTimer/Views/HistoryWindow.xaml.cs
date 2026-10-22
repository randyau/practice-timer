using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeTimer.Core.Sessions;

namespace PracticeTimer.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryStore _history;

    public HistoryWindow(HistoryStore history)
    {
        InitializeComponent();
        _history = history;
        Reload();
    }

    private static string Fmt(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private void Reload()
    {
        SessionGrid.ItemsSource = _history.Sessions.OrderByDescending(s => s.Start).Select(s => new
        {
            s.Id, Date = s.Start.ToString("g"), s.Start, Duration = Fmt(s.DurationSeconds), Seconds = s.DurationSeconds, s.Mode, s.Instrument, s.Note,
        }).ToList();
        DayGrid.ItemsSource = HistoryAggregator.Daily(_history.Sessions)
            .Select(d => new { Label = d.Day.ToString("ddd d MMM yyyy"), d.Day, d.Sessions, Total = Fmt(d.Seconds), d.Seconds }).ToList();
        WeekGrid.ItemsSource = HistoryAggregator.Weekly(_history.Sessions)
            .Select(w => new { Label = w.WeekStart.ToString("d MMM yyyy"), w.WeekStart, w.Sessions, Total = Fmt(w.Seconds), w.Seconds }).ToList();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Guid id) return;
        if (MessageBox.Show(this, "Delete this session?", "History", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _history.Delete(id);
        Reload();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = "practice-history.csv" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // BOM so Excel reads the file as UTF-8.
            File.WriteAllText(dlg.FileName, HistoryCsvWriter.Write(_history.Sessions), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Couldn't write the file (is it open in another program?):\n" + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
