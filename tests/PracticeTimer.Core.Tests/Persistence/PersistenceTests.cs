using PracticeTimer.Core.Metronome;
using PracticeTimer.Core.Persistence;
using PracticeTimer.Core.Sessions;

namespace PracticeTimer.Core.Tests.Persistence;

public abstract class TempDirTest : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "pt-tests-" + Guid.NewGuid().ToString("N"));
    protected TempDirTest() => Directory.CreateDirectory(Dir);
    public void Dispose() => Directory.Delete(Dir, true);
}

public class JsonStoreTests : TempDirTest
{
    public sealed class Doc { public string Name { get; set; } = "x"; public List<int> Items { get; set; } = new(); }

    [Fact]
    public void MissingFileGivesDefault() =>
        Assert.Equal("x", new JsonStore<Doc>(Path.Combine(Dir, "a.json")).Load().Name);

    [Fact]
    public void RoundTripsAndWritesVersionEnvelope()
    {
        var path = Path.Combine(Dir, "a.json");
        var s = new JsonStore<Doc>(path);
        s.Save(new Doc { Name = "hi", Items = { 1, 2 } });
        var d = new JsonStore<Doc>(path).Load();
        Assert.Equal("hi", d.Name);
        Assert.Equal(new[] { 1, 2 }, d.Items);
        Assert.Contains("\"version\": 1", File.ReadAllText(path));
    }

    [Fact]
    public void CreatesMissingDirectoryAndLeavesNoTempFiles()
    {
        var path = Path.Combine(Dir, "sub", "a.json");
        new JsonStore<Doc>(path).Save(new Doc());
        Assert.Equal(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData("not json at all", "a.corrupt-*.json")]
    [InlineData("{\"version\": 1, \"data\": 5}", "a.corrupt-*.json")]
    [InlineData("{\"version\": 99, \"data\": {}}", "a.newer-v99-*.json")]
    public void CorruptOrNewerVersionIsBackedUpAndIgnored(string content, string backup)
    {
        var path = Path.Combine(Dir, "a.json");
        File.WriteAllText(path, content);
        var d = new JsonStore<Doc>(path).Load();
        Assert.Equal("x", d.Name);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Dir, backup));
    }

    [Fact]
    public void OlderVersionIsMigratedWhenAMigratorIsGiven()
    {
        var path = Path.Combine(Dir, "a.json");
        File.WriteAllText(path, "{\"version\": 1, \"data\": {\"Old\": \"carried\"}}");
        var d = new JsonStore<Doc>(path, 2, (v, el) => new Doc { Name = el.GetProperty("Old").GetString()! + v }).Load();
        Assert.Equal("carried1", d.Name);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void OlderVersionWithoutMigratorIsReadAsIs()
    {
        var path = Path.Combine(Dir, "a.json");
        File.WriteAllText(path, "{\"version\": 1, \"data\": {\"Name\": \"kept\"}}");
        Assert.Equal("kept", new JsonStore<Doc>(path, 2).Load().Name);
    }

    [Fact]
    public void UserTextEqualToEnvelopeKeysIsNotRewritten()
    {
        var path = Path.Combine(Dir, "a.json");
        var s = new JsonStore<Doc>(path);
        s.Save(new Doc { Name = "Data", Items = { 1 } });
        Assert.Equal("Data", s.Load().Name);
        s.Save(new Doc { Name = "Version" });
        Assert.Equal("Version", s.Load().Name);
    }

    [Fact]
    public void LockedFileIsNeitherMovedNorTreatedAsCorrupt()
    {
        var path = Path.Combine(Dir, "a.json");
        File.WriteAllText(path, "{\"version\": 1, \"data\": {\"Name\": \"mine\"}}");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal("x", new JsonStore<Doc>(path).Load().Name);
            Assert.True(File.Exists(path));
        }
        Assert.Equal("mine", new JsonStore<Doc>(path).Load().Name);
    }

    [Fact]
    public void ReplacingAnExistingFileKeepsItReadable()
    {
        var path = Path.Combine(Dir, "a.json");
        var s = new JsonStore<Doc>(path);
        for (var i = 0; i < 5; i++) s.Save(new Doc { Name = "n" + i });
        Assert.Equal("n4", s.Load().Name);
        Assert.Equal(new[] { path }, Directory.GetFiles(Dir));
    }
}

public class MetronomeStoreTests : TempDirTest
{
    private static MetronomeProfile P(string name, bool locked) => new()
    {
        Name = name, Locked = locked, MinBpm = 60, MaxBpm = 90, MicDeviceId = "mic1", MicName = "USB",
        Clusters = { new MetronomeCluster { Spectrum = new[] { 0.5, -0.25, 0.125 }, MeanPeakDb = -22.5, Count = 9 } },
    };

    [Fact]
    public void SavesOnlyLockedProfilesAndRoundTripsFields()
    {
        var store = new MetronomeStore(Path.Combine(Dir, "m.json"));
        store.Save(new[] { P("kept", true), P("auto", false) });
        var loaded = Assert.Single(store.Load());
        Assert.Equal("kept", loaded.Name);
        Assert.True(loaded.Locked);
        Assert.Equal("mic1", loaded.MicDeviceId);
        Assert.Equal(new[] { 0.5, -0.25, 0.125 }, loaded.Clusters[0].Spectrum);
        Assert.Equal(-22.5, loaded.Clusters[0].MeanPeakDb);
        Assert.Equal(9, loaded.Clusters[0].Count);
    }

    [Fact]
    public void FlagsProfilesLearnedOnAnotherMic()
    {
        var p = P("m", true);
        Assert.False(MetronomeStore.LearnedOnDifferentMic(p, "mic1"));
        Assert.True(MetronomeStore.LearnedOnDifferentMic(p, "mic2"));
        Assert.False(MetronomeStore.LearnedOnDifferentMic(new MetronomeProfile(), "mic2"));
    }

    [Fact]
    public void CorruptFileLoadsEmpty()
    {
        var path = Path.Combine(Dir, "m.json");
        File.WriteAllText(path, "{{{");
        Assert.Empty(new MetronomeStore(path).Load());
    }
}

public class SettingsStoreTests : TempDirTest
{
    [Fact]
    public void DefaultsMatchSpec()
    {
        var s = new SettingsStore(Path.Combine(Dir, "s.json")).Load();
        Assert.Equal(1, s.StartDelaySeconds);
        Assert.Equal(5, s.StopDelaySeconds);
        Assert.True(s.MetronomeFilterEnabled);
        Assert.True(s.AutoLearnMetronome);
        Assert.Equal("System", s.Theme);
    }

    [Fact]
    public void PersistsChanges()
    {
        var store = new SettingsStore(Path.Combine(Dir, "s.json"));
        var s = store.Load();
        s.DeviceId = "abc"; s.GoalMinutes = 20; s.AlwaysOnTop = true;
        store.Save(s);
        var r = new SettingsStore(Path.Combine(Dir, "s.json")).Load();
        Assert.Equal(("abc", 20, true), (r.DeviceId, r.GoalMinutes, r.AlwaysOnTop));
    }
}

public class HistoryTests : TempDirTest
{
    private static SessionRecord S(string date, double secs, string? inst = null, string? note = null) => new()
    {
        Start = DateTime.Parse(date), DurationSeconds = secs, Mode = "Auto", Instrument = inst, Note = note,
    };

    [Fact]
    public void AddPersistsAndRemembersInstrumentsMostRecentFirst()
    {
        var path = Path.Combine(Dir, "h.json");
        var h = new HistoryStore(path);
        h.Add(S("2026-10-01 10:00", 600, "violin"));
        h.Add(S("2026-10-02 10:00", 600, "piano"));
        h.Add(S("2026-10-03 10:00", 600, "violin"));
        var r = new HistoryStore(path);
        Assert.Equal(3, r.Sessions.Count);
        Assert.Equal(new[] { "violin", "piano" }, r.Instruments);
    }

    [Fact]
    public void DeleteRemovesSession()
    {
        var h = new HistoryStore(Path.Combine(Dir, "h.json"));
        var s = S("2026-10-01 10:00", 60);
        h.Add(s); h.Add(S("2026-10-02 10:00", 60));
        h.Delete(s.Id);
        Assert.Single(h.Sessions);
    }

    [Fact]
    public void DailyTotalsGroupByDateNewestFirst()
    {
        var days = HistoryAggregator.Daily(new[] { S("2026-10-01 09:00", 600), S("2026-10-01 18:00", 300), S("2026-10-03 08:00", 100) });
        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 3), days[0].Day);
        Assert.Equal(900, days[1].Seconds);
        Assert.Equal(2, days[1].Sessions);
    }

    [Fact]
    public void WeeklyTotalsStartOnMonday()
    {
        // 2026-10-04 is a Sunday, 2026-10-05 a Monday.
        var weeks = HistoryAggregator.Weekly(new[] { S("2026-10-04 09:00", 100), S("2026-10-05 09:00", 200), S("2026-10-11 09:00", 50) });
        Assert.Equal(2, weeks.Count);
        Assert.Equal(new DateOnly(2026, 10, 5), weeks[0].WeekStart);
        Assert.Equal(250, weeks[0].Seconds);
        Assert.Equal(new DateOnly(2026, 9, 28), weeks[1].WeekStart);
    }

    [Fact]
    public void TodayTotalOnlyCountsToday() =>
        Assert.Equal(TimeSpan.FromSeconds(60),
            HistoryAggregator.Today(new[] { S("2026-10-05 09:00", 60), S("2026-10-04 09:00", 500) }, new DateTime(2026, 10, 5, 20, 0, 0)));

    [Fact]
    public void CsvQuotesFieldsAndIncludesHeader()
    {
        var csv = HistoryCsvWriter.Write(new[] { S("2026-10-05 09:30:15", 125, "violin", "hard, \"fast\" bit") });
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("Date,Start,DurationSeconds,Duration,Mode,Instrument,Note", lines[0]);
        Assert.Equal("2026-10-05,09:30:15,125,00:02:05,Auto,violin,\"hard, \"\"fast\"\" bit\"", lines[1]);
    }

    [Fact]
    public void CsvDurationDoesNotWrapPastADayAndFormulasAreDefused()
    {
        var csv = HistoryCsvWriter.Write(new[] { S("2026-10-05 09:30:15", 26 * 3600 + 61, "=SUM(A1)", "-2+3") });
        var line = csv.Split("\r\n")[1];
        Assert.Contains(",26:01:01,", line);
        Assert.EndsWith(",'=SUM(A1),'-2+3", line);
    }

    [Fact]
    public void FailedSaveKeepsTheSessionInMemoryAndReportsIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "h.json");
        try
        {
            var h = new HistoryStore(path);
            Exception? failure = null;
            h.SaveFailed += e => failure = e;
            Directory.CreateDirectory(path + ".tmp"); // a directory where the temp file goes: the write fails
            h.Add(S("2026-10-05 09:00", 60));
            Assert.NotNull(failure);
            Assert.Single(h.Sessions);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class SettingsSanitizeTests : TempDirTest
{
    [Fact]
    public void DamagedValuesAreClampedOnLoad()
    {
        var path = Path.Combine(Dir, "s.json");
        File.WriteAllText(path, "{\"version\":1,\"data\":{\"StopDelaySeconds\":-4,\"StartDelaySeconds\":1e999,\"GoalMinutes\":-3,\"Theme\":\"Neon\",\"Mode\":\"x\"}}");
        var s = new SettingsStore(path).Load();
        Assert.Equal(1, s.StopDelaySeconds);
        Assert.True(double.IsFinite(s.StartDelaySeconds));
        Assert.Equal(0, s.GoalMinutes);
        Assert.Equal("System", s.Theme);
        Assert.Equal("Auto", s.Mode);
    }

    [Fact]
    public void NaNFallsBackToTheDefault()
    {
        var s = new AppSettings { StopDelaySeconds = double.NaN, MinTriggerDb = double.PositiveInfinity };
        s.Sanitize();
        Assert.Equal(5, s.StopDelaySeconds);
        Assert.Equal(-55, s.MinTriggerDb);
    }
}
