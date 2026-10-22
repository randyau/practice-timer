namespace PracticeTimer.Core.Persistence;

public sealed class AppSettings
{
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string Mode { get; set; } = "Auto";
    public double StartDelaySeconds { get; set; } = 1;
    public double StopDelaySeconds { get; set; } = 5;
    public double SensitivityMarginDb { get; set; } = 12;
    public double MinTriggerDb { get; set; } = -55;
    public double DutyThreshold { get; set; } = 0.20;
    public bool MetronomeFilterEnabled { get; set; } = true;
    public bool AutoLearnMetronome { get; set; } = true;
    public int GoalMinutes { get; set; }
    public bool ChimeEnabled { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartMinimized { get; set; }
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public bool LargeText { get; set; }
    public string? LastInstrument { get; set; }

    /// <summary>
    /// Pulls every numeric value into a range the app can use, so a hand-edited or damaged file
    /// (NaN, negative delays, absurd levels) can't crash startup or silence the detector.
    /// </summary>
    public void Sanitize()
    {
        StartDelaySeconds = Clamp(StartDelaySeconds, 0.2, 30, 1);
        StopDelaySeconds = Clamp(StopDelaySeconds, 1, 600, 5);
        SensitivityMarginDb = Clamp(SensitivityMarginDb, 4, 30, 12);
        MinTriggerDb = Clamp(MinTriggerDb, -90, -10, -55);
        DutyThreshold = Clamp(DutyThreshold, 0.02, 1, 0.20);
        GoalMinutes = Math.Clamp(GoalMinutes, 0, 24 * 60);
        if (Mode is not ("Auto" or "Manual")) Mode = "Auto";
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}

public sealed class SettingsStore
{
    private readonly JsonStore<AppSettings> _store;
    public SettingsStore(string path) => _store = new JsonStore<AppSettings>(path);
    public AppSettings Load()
    {
        var s = _store.Load();
        s.Sanitize();
        return s;
    }
    public void Save(AppSettings s) => _store.Save(s);
}
