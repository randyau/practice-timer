using System.Collections.Concurrent;
using System.Numerics;

namespace PracticeTimer.Core.Analysis;

/// <summary>Level-invariant spectral fingerprint: log band energies, mean-subtracted, L2-normalised.</summary>
public sealed class BandSpectrum
{
    public const int Bands = 24;
    private readonly int _frameSamples;
    private readonly int _fft;
    private readonly double[] _window;
    private readonly int[] _edges; // bin index per band edge
    private readonly Complex[] _buf;

    public BandSpectrum(int sampleRate, int frameSamples, double loHz = 100, double hiHz = 7500)
    {
        _frameSamples = frameSamples;
        _fft = 1;
        while (_fft < frameSamples) _fft <<= 1;
        _buf = new Complex[_fft];
        _window = new double[frameSamples];
        for (var i = 0; i < frameSamples; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (frameSamples - 1));
        _edges = Edges(_fft, sampleRate, loHz, hiHz);
    }

    private static readonly ConcurrentDictionary<(int, int), int[]> EdgeCache = new();

    private static int[] Edges(int fft, int sampleRate, double loHz = 100, double hiHz = 7500) =>
        EdgeCache.GetOrAdd((fft, sampleRate), _ =>
        {
            hiHz = Math.Min(hiHz, sampleRate * 0.45);
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

    public double[] Compute(float[] samples) => FromEnergies(Energies(samples));

    /// <summary>Linear band energies. Sum these across a burst's frames before <see cref="FromEnergies"/>
    /// so the fingerprint does not depend on where the click falls against the frame grid.</summary>
    public double[] Energies(float[] samples)
    {
        for (var i = 0; i < _fft; i++)
            _buf[i] = i < _frameSamples && i < samples.Length ? samples[i] * _window[i] : Complex.Zero;
        Fft(_buf);
        var v = new double[Bands];
        for (var b = 0; b < Bands; b++)
            for (var k = _edges[b]; k < _edges[b + 1] && k < _fft / 2; k++)
                v[b] += _buf[k].Real * _buf[k].Real + _buf[k].Imaginary * _buf[k].Imaginary;
        return v;
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
