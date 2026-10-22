namespace PracticeTimer.Core.Analysis;

public sealed class LoudnessGate
{
    private bool _open;

    public double MarginDb { get; set; } = 12;
    public double AbsoluteMinDb { get; set; } = -55;
    public double HysteresisDb { get; set; } = 3;

    /// <summary>Margin and absolute minimum that put the trigger level at <paramref name="targetDb"/>, within limits.</summary>
    public static (double MarginDb, double AbsoluteMinDb) SettingsFor(double targetDb, double floorDb) =>
        (Math.Clamp(targetDb - floorDb, 4, 30), Math.Min(-55, targetDb));

    public double EnterThreshold(double floorDb) => Math.Max(floorDb + MarginDb, AbsoluteMinDb);
    public double ExitThreshold(double floorDb) => EnterThreshold(floorDb) - HysteresisDb;

    public bool Process(double dbfs, double floorDb)
    {
        _open = _open ? dbfs > ExitThreshold(floorDb) : dbfs > EnterThreshold(floorDb);
        return _open;
    }
}
