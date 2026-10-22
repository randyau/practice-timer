namespace PracticeTimer.Core.Detection;

public sealed class DetectorSettings
{
    public double MarginDb { get; set; } = 12;
    public double AbsoluteMinDb { get; set; } = -55;
    public TimeSpan ShortBurstMax { get; set; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan DutyWindow { get; set; } = TimeSpan.FromSeconds(2.5);
    public double DutyThreshold { get; set; } = 0.20;
    public TimeSpan Hangover { get; set; } = TimeSpan.FromMilliseconds(500);
    public bool MetronomeFilterEnabled { get; set; } = true;
}
