namespace PracticeTimer.Core.Time;

public interface IMonotonicClock
{
    TimeSpan Now { get; }
}

public sealed class StopwatchClock : IMonotonicClock
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Now => _sw.Elapsed;
}
