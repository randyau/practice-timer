using System.Text;
using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Pipeline;
using PracticeTimer.Core.Timing;

namespace PracticeTimer.Core.Tests.Support;

public sealed record ReplayResult(double Seconds, string Timeline, List<string> Rows, GuidedLearnSession? Learn, double CreditedSeconds);

/// <summary>Plays a recording through the real pipeline the way the app hears it: 10 ms chunks at the recording's own rate.</summary>
public static class ReplayRunner
{
    /// <param name="learnAt">Start a guided metronome learn at this time, if given.</param>
    /// <param name="auto">Run the clock in Auto-listen mode and report the time it credited.</param>
    public static ReplayResult Run(float[] samples, int rate, FingerprintBank bank, bool auto = false, double? learnAt = null, DetectorSettings? settings = null)
    {
        var time = new FakeClock();
        var clock = new PracticeClock(time);
        var pipeline = new AudioPipeline(rate, clock, settings ?? new DetectorSettings(), bank);
        GuidedLearnSession? finished = null;
        pipeline.LearnFinished += s => finished = s;
        if (auto) clock.StartAuto();

        var chunk = rate / 100;
        var learning = false;
        var timeline = new StringBuilder();
        var rows = new List<string>();
        double peak = -120; var tag = FrameTag.Quiet;
        for (var i = 0; i + chunk <= samples.Length; i += chunk)
        {
            var t = (double)i / rate;
            if (learnAt is { } at && !learning && t >= at) { pipeline.StartLearn(); learning = true; rows.Add($"{t,7:0.00}s  -- learn started --"); }
            pipeline.Push(samples.AsSpan(i, chunk));
            time.Advance(0.01);
            clock.Tick();
            var s = pipeline.TakeSnapshot();
            peak = Math.Max(peak, s.Dbfs);
            if (s.Tag > tag) tag = s.Tag;
            if ((i / chunk) % 10 != 9) continue;
            timeline.Append(tag switch { FrameTag.Active => 'A', FrameTag.Metronome => 'm', FrameTag.Loud => 'L', _ => '.' });
            rows.Add($"{t + 0.01,7:0.00}s  peak {peak,6:0.0}  trig {s.ThresholdDb,6:0.0}  floor {s.FloorDb,6:0.0}  {tag}");
            peak = -120; tag = FrameTag.Quiet;
        }
        return new ReplayResult((double)samples.Length / rate, timeline.ToString(), rows, finished, clock.CreditedElapsed.TotalSeconds);
    }

    /// <summary>Teaches <paramref name="bank"/> the metronome in a recording, as a user pressing "Learn metronome" a second in.</summary>
    public static GuidedLearnSession? Teach(float[] samples, int rate, FingerprintBank bank) =>
        Run(samples, rate, bank, learnAt: 1.0).Learn;
}
