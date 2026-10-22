using PracticeTimer.Core.Sessions;
using PracticeTimer.Core.Tests.Support;
using PracticeTimer.Core.Timing;

namespace PracticeTimer.Core.Tests.Persistence;

public class SessionRecorderTests : TempDirTest
{
    private readonly FakeClock _time = new();
    private DateTime _wall = new(2026, 10, 5, 16, 0, 0);
    private readonly PracticeClock _clock;
    private readonly HistoryStore _history;
    private readonly SessionRecorder _rec;

    public SessionRecorderTests()
    {
        _clock = new PracticeClock(_time);
        _history = new HistoryStore(Path.Combine(Dir, "h.json"));
        _rec = new SessionRecorder(_history, () => _wall);
        _rec.Attach(_clock);
    }

    [Fact]
    public void SavesManualSessionWithStartTimeAndDuration()
    {
        _clock.Start();
        _wall = _wall.AddMinutes(10); _time.Advance(600);
        var r = _rec.Save(_clock, "violin", "scales");
        Assert.NotNull(r);
        Assert.Equal(new DateTime(2026, 10, 5, 16, 0, 0), r!.Start);
        Assert.Equal(600, r.DurationSeconds, 1);
        Assert.Equal(("Manual", "violin", "scales"), (r.Mode, r.Instrument, r.Note));
        Assert.Single(_history.Sessions);
    }

    [Fact]
    public void AutoSessionIsLabelledAuto()
    {
        _clock.StartAuto();
        for (var i = 0; i < 200; i++) _clock.OnActivity(TimeSpan.FromSeconds(i * 0.05), true);
        Assert.Equal("Auto", _rec.Save(_clock, null, null)!.Mode);
    }

    [Fact]
    public void TinySessionsAreNotSaved()
    {
        _clock.Start(); _time.Advance(1);
        Assert.Null(_rec.Save(_clock, null, null));
        Assert.Empty(_history.Sessions);
    }

    [Fact]
    public void StartTimeResetsBetweenSessions()
    {
        _clock.Start(); _time.Advance(60); _rec.Save(_clock, null, null); _clock.Reset();
        _wall = _wall.AddHours(1);
        _clock.Start(); _time.Advance(60);
        Assert.Equal(new DateTime(2026, 10, 5, 17, 0, 0), _rec.Save(_clock, null, null)!.Start);
    }
}
