using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Metronome;

public static class TickClusterer
{
    public const double Threshold = 0.9;

    /// <summary>Greedy grouping of tick spectra; each tick joins the first cluster it resembles.</summary>
    public static List<MetronomeCluster> Cluster(IEnumerable<TickSample> ticks)
    {
        var clusters = new List<MetronomeCluster>();
        foreach (var t in ticks)
        {
            var c = clusters.FirstOrDefault(x => BandSpectrum.Cosine(x.Spectrum, t.Spectrum) >= Threshold);
            if (c == null)
            {
                clusters.Add(new MetronomeCluster { Spectrum = t.Spectrum, MeanPeakDb = t.PeakDb, Count = 1 });
                continue;
            }
            c.Spectrum = BandSpectrum.Merge(c.Spectrum, c.Count, t.Spectrum, 1);
            c.MeanPeakDb = (c.MeanPeakDb * c.Count + t.PeakDb) / (c.Count + 1);
            c.Count++;
        }
        return clusters;
    }

    /// <summary>Median-free steadiness check: all onset gaps within tolerance of their mean, mean in range.</summary>
    public static double? SteadyIntervalSeconds(IReadOnlyList<TickSample> ticks, double tolerance, double min = 0.24, double max = 2.0)
    {
        if (ticks.Count < 2) return null;
        var gaps = Enumerable.Range(1, ticks.Count - 1).Select(i => (ticks[i].Onset - ticks[i - 1].Onset).TotalSeconds).ToList();
        var mean = gaps.Average();
        if (mean < min || mean > max || gaps.Any(g => Math.Abs(g - mean) > tolerance * mean)) return null;
        return mean;
    }
}
