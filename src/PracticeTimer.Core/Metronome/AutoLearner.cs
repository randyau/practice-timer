namespace PracticeTimer.Core.Metronome;

/// <summary>Fallback learner: a steady run of clean, similar ticks becomes a tentative profile.</summary>
public sealed class AutoLearner
{
    /// <summary>Must exceed <see cref="GuidedLearnSession.RequiredTicks"/>: a guess needs more evidence than a taught sound.</summary>
    public const int Window = GuidedLearnSession.RequiredTicks + 4;
    private readonly FingerprintBank _bank;
    private readonly List<TickSample> _recent = new();

    public AutoLearner(FingerprintBank bank) => _bank = bank;

    public bool Enabled { get; set; } = true;

    public void OnCleanTick(CleanTick tick)
    {
        if (!Enabled || tick.Matched) return;
        _recent.Add(tick.Sample);
        if (_recent.Count > Window) _recent.RemoveAt(0);
        if (_recent.Count < Window) return;

        var interval = TickClusterer.SteadyIntervalSeconds(_recent, 0.12);
        if (interval == null) return;
        var clusters = TickClusterer.Cluster(_recent);
        if (clusters.Count > 2 || clusters.Any(c => c.Count < 2)) return;

        var bpm = 60 / interval.Value;
        _bank.TryAdd(new MetronomeProfile { Name = "Metronome (auto)", Locked = false, Clusters = clusters, MinBpm = bpm, MaxBpm = bpm });
        _recent.Clear();
    }
}
