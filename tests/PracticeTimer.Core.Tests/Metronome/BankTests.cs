using PracticeTimer.Core.Analysis;
using PracticeTimer.Core.Metronome;
using static PracticeTimer.Core.Tests.Signals.SignalGen;

namespace PracticeTimer.Core.Tests.Metronome;

public class TickClustererTests
{
    private static readonly BandSpectrum Spec = new(Rate, 400);
    private static double[] Of(float[] a) => Spec.Compute(a.Concat(new float[400]).Take(400).ToArray());
    private static TickSample T(float[] a, double db = -20) => new(TimeSpan.Zero, Of(a), db, TimeSpan.FromMilliseconds(25));

    [Fact]
    public void GroupsSimilarTicksAndSeparatesDifferentOnes()
    {
        var ticks = new List<TickSample>();
        for (var i = 0; i < 4; i++) { ticks.Add(T(Click(-20, 2000))); ticks.Add(T(Click(-20, 500))); }
        var c = TickClusterer.Cluster(ticks);
        Assert.Equal(2, c.Count);
        Assert.All(c, x => Assert.Equal(4, x.Count));
    }
}

public class FingerprintBankTests
{
    private static readonly BandSpectrum Spec = new(Rate, 400);
    private static double[] Of(float[] a) => Spec.Compute(a.Concat(new float[400]).Take(400).ToArray());

    private static MetronomeProfile Profile(double freq, string name = "m", bool locked = true, double db = -20) => new()
    {
        Name = name, Locked = locked,
        Clusters = { new MetronomeCluster { Spectrum = Of(Click(db, freq)), MeanPeakDb = db, Count = 8 } },
    };

    private static bool Matches(FingerprintBank b, double freq, double db = -20) =>
        b.Match(Of(Click(db, freq)), db) != null;

    [Fact]
    public void MatchesLearnedSoundOnly()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000));
        Assert.True(Matches(b, 2000));
        Assert.False(Matches(b, 500));
    }

    [Fact]
    public void MatchToleratesLevelChangesWithinBandOnly()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000));
        Assert.True(Matches(b, 2000, -28));
        Assert.False(Matches(b, 2000, -50));
    }

    [Fact]
    public void DisabledProfilesDoNotMatch()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000)).Profile.Enabled = false;
        Assert.False(Matches(b, 2000));
    }

    [Fact]
    public void LearningAKnownSoundMergesInsteadOfDuplicating()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000, "first"));
        var r = b.TryAdd(Profile(2000, "second"));
        Assert.Equal(AddKind.Merged, r.Kind);
        Assert.Single(b.Profiles);
        Assert.Equal("first", r.Profile.Name);
        Assert.Equal(16, r.Profile.Clusters[0].Count);
    }

    [Fact]
    public void MergingLockedIntoTentativePromotesIt()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000, "auto", locked: false));
        var r = b.TryAdd(Profile(2000, "mine", locked: true));
        Assert.True(r.Profile.Locked);
        Assert.Equal("mine", r.Profile.Name);
    }

    [Fact]
    public void FullBankEvictsLeastRecentlyMatchedTentativeFirst()
    {
        var b = new FingerprintBank();
        var freqs = new[] { 300.0, 900, 2200, 4500 };
        b.TryAdd(Profile(freqs[0], "locked", true));
        b.TryAdd(Profile(freqs[1], "t1", false));
        b.TryAdd(Profile(freqs[2], "t2", false));
        b.TryAdd(Profile(freqs[3], "t3", false));
        Matches(b, freqs[1]); // t1 used recently, t2 stalest
        var r = b.TryAdd(Profile(1400, "new", true));
        Assert.Equal(AddKind.Added, r.Kind);
        Assert.DoesNotContain(b.Profiles, p => p.Name == "t2");
        Assert.Contains(b.Profiles, p => p.Name == "locked");
    }

    [Fact]
    public void FullOfLockedProfilesReportsFull()
    {
        var b = new FingerprintBank();
        foreach (var f in new[] { 300.0, 900, 2200, 4500 }) b.TryAdd(Profile(f));
        Assert.Equal(AddKind.Full, b.TryAdd(Profile(1400)).Kind);
        Assert.Equal(4, b.Profiles.Count);
    }

    [Fact]
    public void TentativeProfileMatchingSustainedSoundIsDiscarded()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000, "auto", locked: false));
        var sustained = Of(Click(-20, 2000));
        for (var i = 0; i < 3; i++) b.NoteSustained(sustained);
        Assert.Empty(b.Profiles);
    }

    [Fact]
    public void LockedProfileSurvivesSustainedMatches()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(2000));
        for (var i = 0; i < 5; i++) b.NoteSustained(Of(Click(-20, 2000)));
        Assert.Single(b.Profiles);
    }

    [Fact]
    public void ClearTentativeKeepsLocked()
    {
        var b = new FingerprintBank();
        b.TryAdd(Profile(300, "l")); b.TryAdd(Profile(2000, "t", false));
        b.ClearTentative();
        Assert.Equal("l", Assert.Single(b.Profiles).Name);
    }
}
