namespace PracticeTimer.Core.Analysis;

/// <summary>Low-percentile level of recent quiet frames. Callers add only frames the gate calls quiet.</summary>
public sealed class NoiseFloorEstimator
{
    public const double DefaultFloorDb = -60;
    private const int MinSamples = 20;
    private const double Low = 0.10, High = 0.90;
    private readonly Queue<double> _window = new();
    private readonly double[] _scratch;
    private readonly int _max;
    private double _floor = DefaultFloorDb;
    private double _bias;

    public NoiseFloorEstimator(int windowFrames = 400)
    {
        _max = windowFrames;
        _scratch = new double[windowFrames + 1];
    }

    public double Floor => _floor;

    public void Add(double dbfs)
    {
        _window.Enqueue(dbfs);
        while (_window.Count > _max) _window.Dequeue();
        if (_window.Count >= MinSamples) _floor = Estimate();
    }

    /// <summary>
    /// Sets the floor from a stretch of room quiet at the high percentile, so ordinary room variation stays below the
    /// trigger. The spread between the high and low percentiles is kept as a bias, so later frames drawn from the same
    /// room keep the floor where calibration put it instead of sliding down to the quietest tenth.
    /// </summary>
    public void Calibrate(IReadOnlyList<double> quietFrames)
    {
        if (quietFrames.Count == 0) return;
        _window.Clear();
        foreach (var f in quietFrames.TakeLast(_max)) _window.Enqueue(f);
        var n = Fill();
        _bias = Math.Max(0, Percentile(n, High) - Percentile(n, Low));
        _floor = Estimate();
    }

    private double Estimate()
    {
        var n = Fill();
        return Percentile(n, Low) + _bias;
    }

    private int Fill()
    {
        _window.CopyTo(_scratch, 0);
        Array.Sort(_scratch, 0, _window.Count);
        return _window.Count;
    }

    private double Percentile(int n, double p) => _scratch[(int)Math.Floor(p * (n - 1))];
}
