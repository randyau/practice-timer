namespace PracticeTimer.Core.Tests.Signals;

/// <summary>Synthetic audio at 16 kHz mono. Levels are dBFS (RMS for tones, peak for clicks and beeps).</summary>
public static class SignalGen
{
    public const int Rate = 16000;
    private static int N(double sec) => (int)Math.Round(sec * Rate);
    private static float Amp(double db) => (float)Math.Pow(10, db / 20);

    public static float[] Silence(double sec, double noiseDb = -75, int seed = 1)
    {
        var rnd = new Random(seed);
        var a = new float[N(sec)];
        var amp = Amp(noiseDb) * (float)Math.Sqrt(3); // uniform noise rms = amp/sqrt(3)
        for (var i = 0; i < a.Length; i++) a[i] = amp * (float)(rnd.NextDouble() * 2 - 1);
        return a;
    }

    public static float[] Tone(double freq, double sec, double rmsDb)
    {
        var a = new float[N(sec)];
        var amp = Amp(rmsDb) * (float)Math.Sqrt(2);
        var ramp = Math.Min(a.Length / 2, N(0.005));
        for (var i = 0; i < a.Length; i++)
        {
            var env = i < ramp ? (double)i / ramp : i >= a.Length - ramp ? (double)(a.Length - i) / ramp : 1;
            a[i] = (float)(amp * env * Math.Sin(2 * Math.PI * freq * i / Rate));
        }
        return a;
    }

    /// <summary>A "note": fundamental plus harmonics, richer than a pure tone.</summary>
    public static float[] Note(double freq, double sec, double rmsDb)
    {
        var a = Tone(freq, sec, rmsDb - 1);
        var h2 = Tone(freq * 2, sec, rmsDb - 7);
        var h3 = Tone(freq * 3, sec, rmsDb - 11);
        return Mix(a, h2, h3);
    }

    /// <summary>Short damped click (mechanical-style tick).</summary>
    public static float[] Click(double peakDb = -20, double freq = 2000, double tauMs = 4, double lengthMs = 20)
    {
        var a = new float[N(lengthMs / 1000)];
        var amp = Amp(peakDb);
        for (var i = 0; i < a.Length; i++)
        {
            var t = (double)i / Rate;
            a[i] = (float)(amp * Math.Exp(-t / (tauMs / 1000)) * Math.Sin(2 * Math.PI * freq * t));
        }
        return a;
    }

    /// <summary>Digital metronome beep.</summary>
    public static float[] Beep(double freq = 1000, double ms = 50, double peakDb = -20) =>
        Tone(freq, ms / 1000, peakDb - 3);

    public static float[] ClickTrain(double bpm, double sec, Func<float[]> click, double offset = 0) =>
        ClickTrain(_ => bpm, sec, _ => click(), offset);

    public static float[] ClickTrain(Func<double, double> bpmAt, double sec, Func<int, float[]> click, double offset = 0)
    {
        var a = new float[N(sec)];
        var t = offset;
        for (var k = 0; t < sec; k++)
        {
            var c = click(k);
            var s = N(t);
            for (var i = 0; i < c.Length && s + i < a.Length; i++) a[s + i] += c[i];
            t += 60.0 / bpmAt(t);
        }
        return a;
    }

    public static float[] Mix(params float[][] parts)
    {
        var a = new float[parts.Max(p => p.Length)];
        foreach (var p in parts) for (var i = 0; i < p.Length; i++) a[i] += p[i];
        return a;
    }

    public static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>Noise floor under the whole signal.</summary>
    public static float[] WithRoom(float[] signal, double noiseDb = -75, int seed = 1) =>
        Mix(Silence((double)signal.Length / Rate, noiseDb, seed), signal);

    /// <summary>Feeds audio in odd-sized chunks, as a capture callback would.</summary>
    public static IEnumerable<float[]> Chunks(float[] audio, int size = 441)
    {
        for (var i = 0; i < audio.Length; i += size)
            yield return audio.AsSpan(i, Math.Min(size, audio.Length - i)).ToArray();
    }
}
