using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Persistence;
using PracticeTimer.ViewModels;

namespace PracticeTimer.Views;

public sealed class MetronomeItem : ObservableBase
{
    public MetronomeItem(MetronomeProfile profile, string? currentMic) { Profile = profile; CurrentMic = currentMic; }
    public MetronomeProfile Profile { get; }
    private string? CurrentMic { get; }
    public string Name { get => Profile.Name; set { Profile.Name = value; Raise(); } }
    public bool Enabled { get => Profile.Enabled; set { Profile.Enabled = value; Raise(); } }

    public string Info
    {
        get
        {
            var bpm = Profile.MaxBpm > 0 ? (Math.Abs(Profile.MaxBpm - Profile.MinBpm) < 1 ? $"~{Profile.MinBpm:0} BPM" : $"{Profile.MinBpm:0}-{Profile.MaxBpm:0} BPM") : "tempo unknown";
            var mic = Profile.MicName != null ? $", learned on {Profile.MicName}" : "";
            var warn = MetronomeStore.LearnedOnDifferentMic(Profile, CurrentMic) ? " (different mic selected: re-learn if it isn't recognised)" : "";
            return $"{bpm}, {Profile.CreatedUtc.ToLocalTime():d}{mic}{warn}";
        }
    }
}

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _original;
    private readonly ObservableCollection<MetronomeItem> _items = new();
    private readonly HashSet<string> _removed = new();

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _original = vm.Settings;
        DataContext = _working = Clone(vm.Settings);
        foreach (var p in vm.Bank.Profiles.Where(p => p.Locked)) _items.Add(new MetronomeItem(p, vm.SelectedDevice?.Id));
        MetroList.ItemsSource = _items;
        Closed += (_, _) => DataContext = null; // AppSettings isn't INPC; a live binding to it would keep this window alive
    }

    private readonly AppSettings _working;

    private static AppSettings Clone(AppSettings s) => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        _vm.Calibrate();
        MessageBox.Show(this, "Calibrating for 3 seconds. Keep the room quiet, then close this message.", "Calibrate", MessageBoxButton.OK);
    }

    private void WindowsSound_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });

    private void DeleteMetronome_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not MetronomeItem item) return;
        _removed.Add(item.Profile.Id);
        _items.Remove(item);
    }

    private void ForgetAll_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;
        var r = MessageBox.Show(this, "Forget every saved metronome? This takes effect when you press OK.", "Forget all",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        foreach (var item in _items) _removed.Add(item.Profile.Id);
        _items.Clear();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var prop in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
            prop.SetValue(_original, prop.GetValue(_working));
        // Metronome edits were made on copies; they reach the live bank only here, so Cancel really cancels.
        _vm.Bank.ApplyEdits(_items.Select(i => new ProfileEdit(i.Profile.Id, i.Name, i.Enabled, false))
            .Concat(_removed.Select(id => new ProfileEdit(id, "", false, true))));
        _vm.SaveMetronomes();
        _vm.ApplySettings();
        DialogResult = true;
    }
}
