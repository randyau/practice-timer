using PracticeTimer.Core.Analysis;

namespace PracticeTimer.Core.Detection;

public enum FrameTag { Quiet, Loud, Metronome, Active }

public readonly record struct ActivitySample(TimeSpan Time, bool Active, FrameTag Tag, double Dbfs);

public interface IActivityDetector
{
    ActivitySample Process(AudioFrame frame);
    void Reset();
}
