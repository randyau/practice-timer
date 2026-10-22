using System.Collections.Concurrent;
using System.Numerics;

namespace PracticeTimer.Core.Analysis;

/// <summary>Level-invariant spectral fingerprint: log band energies, mean-subtracted, L2-normalised.</summary>
public static class BandSpectrum
{
    public const int Bands = 24;
    public const double DefaultLoHz = 100;
    public const double DefaultHiHz = 7500;

    private static readonly ConcurrentDictionary<(int, int, double, double), int[]> EdgeCache = new();

    /// <summary>Upper band edge actually used at a sample rate; it stays at the default unless the rate is too low for it.</summary>
    public static double EffectiveHiHz(int sampleRate, double hiHz = DefaultHiHz) => Math.Min(hiHz, sampleRate * 0.45);

    private static int[] Edges(int fft, int sampleRate, double loHz = DefaultLoHz, double hiHz = DefaultHiHz) =>
        EdgeCache.GetOrAdd((fft, sampleRate, loHz, hiHz), _ =>
        {
            hiHz = EffectiveHiHz(sampleRate, hiHz);
            var edges = new int[Bands + 1];
            var binHz = (double)sampleRate / fft;
            for (var b = 0; b <= Bands; b++)
            {
                var hz = loHz * Math.Pow(hiHz / loHz, (double)b / Bands);
                edges[b] = Math.Max(1, (int)Math.Round(hz / binHz));
                if (b > 0 && edges[b] <= edges[b - 1]) edges[b] = edges[b - 1] + 1;
            }
            return edges;
        });

    /// <summary>
    /// Fingerprint of a whole burst from one rectangular-window FFT over its frames. Analysing the burst as a unit,
    /// not frame by frame, keeps the shape independent of where a click falls against the frame grid.
    /// </summary>
    public static double[] BurstFingerprint(float[] samples, int sampleRate)
    {
        var fft = 256;
        while (fft < samples.Length) fft <<= 1;
        var buf = new Complex[fft];
        for (var i = 0; i < samples.Length; i++) buf[i] = samples[i];
        Fft(buf);
        var edges = Edges(fft, sampleRate);
        var v = new double[Bands];
        for (var b = 0; b < Bands; b++)
            for (var k = edges[b]; k < edges[b + 1] && k < fft / 2; k++)
                v[b] += buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary;
        return FromEnergies(v);
    }

    /// <summary>Log shape with bands clamped 35 dB below the loudest, so noise-only bands don't dominate the match.</summary>
    public static double[] FromEnergies(double[] energies)
    {
        var log = energies.Select(e => Math.Log10(e + 1e-12)).ToArray();
        var floor = log.Max() - 3.5;
        return Normalize(log.Select(x => Math.Max(x, floor)).ToArray());
    }

    public static double[] Normalize(double[] v)
    {
        var mean = v.Average();
        var r = v.Select(x => x - mean).ToArray();
        var norm = Math.Sqrt(r.Sum(x => x * x));
        return norm < 1e-12 ? r : r.Select(x => x / norm).ToArray();
    }

    public static double Cosine(double[] a, double[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na < 1e-12 || nb < 1e-12 ? 0 : d / Math.Sqrt(na * nb);
    }

    public static double[] Merge(double[] a, double wa, double[] b, double wb) =>
        Normalize(a.Select((x, i) => x * wa + b[i] * wb).ToArray());

    private static void Fft(Complex[] x)
    {
        var n = x.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var w = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (var i = 0; i < n; i += len)
            {
                var cur = Complex.One;
                for (var k = 0; k < len / 2; k++)
                {
                    var u = x[i + k];
                    var v = x[i + k + len / 2] * cur;
                    x[i + k] = u + v;
                    x[i + k + len / 2] = u - v;
                    cur *= w;
                }
            }
        }
    }
}
