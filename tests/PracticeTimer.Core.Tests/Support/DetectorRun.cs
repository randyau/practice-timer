using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;

namespace PracticeTimer.Core.Tests.Support;

public static class DetectorRun
{
    public static readonly TimeSpan FrameLength = TimeSpan.FromMilliseconds(25);

    public static List<ActivitySample> Run(IActivityDetector d, float[] audio, int rate = 16000)
    {
        var slicer = new FrameSlicer(rate, FrameLength);
        var result = new List<ActivitySample>();
        foreach (var chunk in Signals.SignalGen.Chunks(audio))
            slicer.Push(chunk, f => result.Add(d.Process(f)));
        return result;
    }

    public static double ActiveSeconds(IEnumerable<ActivitySample> s, double from = 0, double to = double.MaxValue) =>
        s.Count(x => x.Active && x.Time.TotalSeconds > from && x.Time.TotalSeconds <= to) * FrameLength.TotalSeconds;

    public static double FirstActive(IEnumerable<ActivitySample> s) =>
        s.Where(x => x.Active).Select(x => x.Time.TotalSeconds).DefaultIfEmpty(double.NaN).First();
}
