namespace PracticeTimer.Core.Analysis;

/// <summary>Low-percentile level of recent quiet frames. Callers add only frames the gate calls quiet.</summary>
public sealed class NoiseFloorEstimator
{
    public const double DefaultFloorDb = -60;
    private const int MinSamples = 20;
    private readonly Queue<double> _window = new();
    private readonly int _max;
    private double _floor = DefaultFloorDb;

    public NoiseFloorEstimator(int windowFrames = 400) => _max = windowFrames;

    public double Floor => _floor;

    public void Add(double dbfs)
    {
        _window.Enqueue(dbfs);
        while (_window.Count > _max) _window.Dequeue();
        if (_window.Count >= MinSamples) _floor = Percentile(_window, 0.10);
    }

    public void Calibrate(IReadOnlyList<double> quietFrames)
    {
        if (quietFrames.Count == 0) return;
        _window.Clear();
        foreach (var f in quietFrames) _window.Enqueue(f);
        _floor = Percentile(quietFrames, 0.90);
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var a = values.OrderBy(x => x).ToArray();
        return a[(int)Math.Floor(p * (a.Length - 1))];
    }
}
