using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Detection;
using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Tests.Signals;

namespace PracticeTimer.Core.Tests.Support;

/// <summary>Detector + bank + learners wired the way the app pipeline wires them.</summary>
public sealed class Harness
{
    public FingerprintBank Bank { get; }
    public LoudnessDetector Detector { get; }
    public AutoLearner Auto { get; }
    public GuidedLearnSession? Session { get; private set; }
    private readonly FrameSlicer _slicer = new(SignalGen.Rate, TimeSpan.FromMilliseconds(25));

    public Harness(FingerprintBank? bank = null, bool autoLearn = false, Action<DetectorSettings>? tweak = null)
    {
        Bank = bank ?? new FingerprintBank();
        var s = new DetectorSettings();
        tweak?.Invoke(s);
        Detector = new LoudnessDetector(s, Bank);
        Auto = new AutoLearner(Bank) { Enabled = autoLearn };
        Detector.CleanTick += t => { Auto.OnCleanTick(t); Session?.OnCleanTick(t); };
    }

    public GuidedLearnSession StartGuided(TimeSpan at) => Session = new GuidedLearnSession(Bank, at);

    public List<ActivitySample> Run(float[] audio, bool addRoom = true)
    {
        var result = new List<ActivitySample>();
        foreach (var chunk in SignalGen.Chunks(addRoom ? SignalGen.WithRoom(audio) : audio))
            _slicer.Push(chunk, f =>
            {
                result.Add(Detector.Process(f));
                Session?.OnFrame(f.End, Detector.SustainedOpen, f.Duration);
            });
        return result;
    }

}
