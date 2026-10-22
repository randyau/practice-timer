using PracticeTimer.Core.Time;

namespace PracticeTimer.Core.Tests.Support;

public sealed class FakeClock : IMonotonicClock
{
    public TimeSpan Now { get; private set; }
    public void Advance(TimeSpan by) => Now += by;
    public void Advance(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
