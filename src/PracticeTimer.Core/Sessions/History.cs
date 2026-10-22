using System.Globalization;
using System.Text;
using PracticeTimer.Core.Persistence;

namespace PracticeTimer.Core.Sessions;

public sealed class SessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Start { get; set; }
    public double DurationSeconds { get; set; }
    public string Mode { get; set; } = "Manual";
    public string? Instrument { get; set; }
    public string? Note { get; set; }
}

public sealed class HistoryStore
{
    public sealed class Data
    {
        public List<SessionRecord> Sessions { get; set; } = new();
        public List<string> Instruments { get; set; } = new();
    }

    private readonly JsonStore<Data> _store;
    private readonly Data _data;

    public HistoryStore(string path)
    {
        _store = new JsonStore<Data>(path);
        _data = _store.Load();
    }

    public IReadOnlyList<SessionRecord> Sessions => _data.Sessions;
    public IReadOnlyList<string> Instruments => _data.Instruments;

    public void Add(SessionRecord s)
    {
        _data.Sessions.Add(s);
        if (!string.IsNullOrWhiteSpace(s.Instrument))
        {
            _data.Instruments.RemoveAll(i => string.Equals(i, s.Instrument, StringComparison.OrdinalIgnoreCase));
            _data.Instruments.Insert(0, s.Instrument.Trim());
        }
        _store.Save(_data);
    }

    public void Delete(Guid id)
    {
        if (_data.Sessions.RemoveAll(s => s.Id == id) > 0) _store.Save(_data);
    }
}

public sealed record DayTotal(DateOnly Day, double Seconds, int Sessions);
public sealed record WeekTotal(DateOnly WeekStart, double Seconds, int Sessions);

public static class HistoryAggregator
{
    public static List<DayTotal> Daily(IEnumerable<SessionRecord> sessions) =>
        sessions.GroupBy(s => DateOnly.FromDateTime(s.Start))
            .Select(g => new DayTotal(g.Key, g.Sum(s => s.DurationSeconds), g.Count()))
            .OrderByDescending(d => d.Day).ToList();

    public static List<WeekTotal> Weekly(IEnumerable<SessionRecord> sessions) =>
        sessions.GroupBy(s => WeekStart(DateOnly.FromDateTime(s.Start)))
            .Select(g => new WeekTotal(g.Key, g.Sum(s => s.DurationSeconds), g.Count()))
            .OrderByDescending(w => w.WeekStart).ToList();

    public static TimeSpan Today(IEnumerable<SessionRecord> sessions, DateTime now) =>
        TimeSpan.FromSeconds(sessions.Where(s => s.Start.Date == now.Date).Sum(s => s.DurationSeconds));

    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}

public static class HistoryCsvWriter
{
    public static string Write(IEnumerable<SessionRecord> sessions)
    {
        var sb = new StringBuilder("Date,Start,DurationSeconds,Duration,Mode,Instrument,Note\r\n");
        foreach (var s in sessions.OrderBy(s => s.Start))
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append(string.Join(",",
                s.Start.ToString("yyyy-MM-dd", inv),
                s.Start.ToString("HH:mm:ss", inv),
                Math.Round(s.DurationSeconds).ToString(inv),
                TimeSpan.FromSeconds(Math.Round(s.DurationSeconds)).ToString("hh\\:mm\\:ss", inv),
                Quote(s.Mode), Quote(s.Instrument), Quote(s.Note))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Quote(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

/// <summary>Remembers when a practice session began and turns the clock's total into a history entry.</summary>
public sealed class SessionRecorder
{
    private readonly HistoryStore _history;
    private readonly Func<DateTime> _now;
    private readonly double _minSeconds;
    private DateTime? _start;
    private bool _auto;

    public SessionRecorder(HistoryStore history, Func<DateTime> now, double minSeconds = 3)
    {
        _history = history;
        _now = now;
        _minSeconds = minSeconds;
    }

    public void Attach(Timing.PracticeClock clock) => clock.StateChanged += state =>
    {
        if (state == Timing.ClockState.Stopped) { _start = null; _auto = false; return; }
        if (state is Timing.ClockState.ManualPaused or Timing.ClockState.PausedQuiet) return;
        _start ??= _now();
        _auto |= state is Timing.ClockState.Listening or Timing.ClockState.Playing;
    };

    /// <summary>Saves the clock's current total as a session, if long enough, and starts a fresh one.</summary>
    public SessionRecord? Save(Timing.PracticeClock clock, string? instrument, string? note)
    {
        var elapsed = clock.Elapsed.TotalSeconds;
        var start = _start ?? _now() - TimeSpan.FromSeconds(elapsed);
        var auto = _auto;
        _start = null;
        _auto = false;
        if (elapsed < _minSeconds) return null;
        var rec = new SessionRecord
        {
            Start = start, DurationSeconds = elapsed, Mode = auto ? "Auto" : "Manual",
            Instrument = string.IsNullOrWhiteSpace(instrument) ? null : instrument.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        _history.Add(rec);
        return rec;
    }
}
