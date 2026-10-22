using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Tests.Support;

// Feeds a recording through the real pipeline, as the app would hear it, and prints what it decided.
//   replay <file.wav> [--auto] [--learn-at <seconds>] [--bank <metronome.wav>] [--rows] [--ticks]
// --auto       run the clock in Auto-listen mode and report the time it credited
// --learn-at   start a guided metronome learn at that time
// --bank       first learn the metronome in that recording, then replay <file.wav> against what was learned
// --ticks      list each clean burst with its spectral similarity to the previous one
// --pink <dB>      lay pink noise at that RMS level over the recording (and over the teaching recording)
// --blower <dB>    lay leaf-blower noise (wandering pink noise) at that average level over the recording
// --repeat <n>     play the recording n times in a row (steady noise needs 10 s or more to be recognised)
// --rows       print one line per 100 ms: time, peak dBFS, trigger dBFS, tag

if (args.Length == 0) { Console.Error.WriteLine("usage: replay <file.wav> [--auto] [--learn-at s] [--bank file.wav] [--ticks] [--rows]"); return 2; }
var file = args[0];
string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
double? learnAt = Opt("--learn-at") is { } l ? double.Parse(l) : null;
float[] Noisy(float[] s) =>
    Opt("--pink") is { } pk ? NoiseGen.Mix(s, NoiseGen.Pink(s.Length, double.Parse(pk)))
    : Opt("--blower") is { } bl ? NoiseGen.Mix(s, NoiseGen.LeafBlower(s.Length, 48000, double.Parse(bl)))
    : s;
var bank = new FingerprintBank();
if (Opt("--bank") is { } bankFile)
{
    var (bs, br) = WavReader.Read(bankFile);
    var taught = ReplayRunner.Teach(Noisy(bs), br, bank);
    Console.WriteLine($"== taught from {bankFile}: {(taught == null ? "not finished" : $"{taught.Status}, {taught.TickCount} ticks, {taught.Bpm:0.0} BPM")}");
}
if (args.Contains("--ticks")) { PrintTicks(file); return 0; }

var (samples, rate) = WavReader.Read(file);
if (Opt("--repeat") is { } rep) samples = Enumerable.Repeat(samples, int.Parse(rep)).SelectMany(x => x).ToArray();
var r = ReplayRunner.Run(Noisy(samples), rate, bank, args.Contains("--auto"), learnAt);
Console.WriteLine($"== {file}\n{r.Seconds:0.0} s at {rate} Hz");
if (args.Contains("--rows")) foreach (var row in r.Rows) Console.WriteLine(row);
Console.WriteLine("per 100 ms: A active, m metronome, L loud-but-ignored, . quiet");
for (var k = 0; k < r.Timeline.Length; k += 100) Console.WriteLine($"  {k / 10.0,5:0}s {r.Timeline.Substring(k, Math.Min(100, r.Timeline.Length - k))}");
if (r.Learn != null || learnAt != null) Console.WriteLine(r.Learn == null ? "learn: still running at end" : $"learn: {r.Learn.Status}, {r.Learn.TickCount} ticks, {r.Learn.Bpm:0.0} BPM");
Console.WriteLine($"bank: {bank.Profiles.Count} profile(s)");
if (args.Contains("--auto")) Console.WriteLine($"credited: {r.CreditedSeconds:0.0} s of {r.Seconds:0.0} s");
return 0;

static void PrintTicks(string path)
{
    var (samples, rate) = WavReader.Read(path);
    var det = new LoudnessDetector(new DetectorSettings(), new FingerprintBank());
    double[]? prev = null;
    det.CleanTick += t =>
    {
        var cos = prev == null ? double.NaN : BandSpectrum.Cosine(prev, t.Burst.Spectrum);
        Console.WriteLine($"  {t.Burst.Onset.TotalSeconds,6:0.00}s  dur {t.Burst.Duration.TotalMilliseconds,4:0} ms  peak {t.Burst.PeakDb,6:0.0}  gap {t.Burst.GapBefore.TotalSeconds,5:0.00}  cos-prev {cos,5:0.00}");
        prev = t.Burst.Spectrum;
    };
    var slicer = new FrameSlicer(rate, TimeSpan.FromMilliseconds(25));
    for (var i = 0; i + rate / 100 <= samples.Length; i += rate / 100) slicer.Push(samples.AsSpan(i, rate / 100), f => det.Process(f));
}
