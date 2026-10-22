namespace PracticeTimer.Core.Analysis;

public static class Dsp
{
    public const double MinDb = -120;

    public static double Dbfs(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return MinDb;
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        var rms = Math.Sqrt(sum / samples.Length);
        return rms <= 0 ? MinDb : Math.Max(MinDb, 20 * Math.Log10(rms));
    }
}
