namespace PracticeTimer.Core.Tests.Support;

/// <summary>Background noise to lay over recordings. Levels are RMS dBFS.</summary>
public static class NoiseGen
{
    /// <summary>Pink noise (equal energy per octave, -3 dB per octave), seeded so runs repeat.</summary>
    public static float[] Pink(int length, double rmsDb, int seed = 7)
    {
        var rnd = new Random(seed);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0; // Paul Kellet's filter
        var x = new double[length];
        for (var i = 0; i < length; i++)
        {
            var w = rnd.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856; b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
            x[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362;
            b6 = w * 0.115926;
        }
        return Scale(x, rmsDb);
    }

    /// <summary>
    /// A leaf blower heard through a wall or a window: pink noise whose level wanders by several dB over seconds and
    /// surges now and then, as the operator swings it about. <paramref name="rmsDb"/> is the average level.
    /// </summary>
    public static float[] LeafBlower(int length, int rate, double rmsDb, int seed = 11)
    {
        var rnd = new Random(seed);
        var pink = Pink(length, 0, seed + 1);
        var out_ = new float[length];
        double gainDb = 0, target = 0;
        var next = 0;
        for (var i = 0; i < length; i++)
        {
            if (i >= next) { target = (rnd.NextDouble() * 2 - 1) * 5; next = i + (int)(rate * (1 + rnd.NextDouble() * 3)); }
            gainDb += (target - gainDb) * (1.0 / (rate * 0.5)); // glide to the next level over about half a second
            out_[i] = pink[i] * (float)Math.Pow(10, gainDb / 20);
        }
        return Scale(out_.Select(v => (double)v).ToArray(), rmsDb);
    }

    public static float[] Mix(float[] signal, float[] noise) =>
        signal.Select((s, i) => Math.Clamp(s + noise[i % noise.Length], -1f, 1f)).ToArray();

    private static float[] Scale(double[] x, double rmsDb)
    {
        var rms = Math.Sqrt(x.Average(v => v * v));
        var k = Math.Pow(10, rmsDb / 20) / rms;
        return x.Select(v => (float)(v * k)).ToArray();
    }
}
