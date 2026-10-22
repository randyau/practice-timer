namespace PracticeTimer.Core.Metronome;

public enum LearnStatus { Idle, Learning, Success, AlreadyKnown, Timeout, TooMuchSound, BankFull, Cancelled }

/// <summary>User-initiated metronome learning. Driven only by frame time, so it is deterministic.</summary>
public sealed class GuidedLearnSession
{
    public const int RequiredTicks = 8;
    private readonly FingerprintBank _bank;
    private readonly TimeSpan _start;
    private readonly List<TickSample> _ticks = new();
    private TimeSpan _sustained;

    public GuidedLearnSession(FingerprintBank bank, TimeSpan startedAt)
    {
        _bank = bank;
        _start = startedAt;
        Status = LearnStatus.Learning;
    }

    public TimeSpan MinDuration { get; set; } = TimeSpan.FromSeconds(4);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaxSustained { get; set; } = TimeSpan.FromSeconds(1);

    public LearnStatus Status { get; private set; }
    public int TickCount => _ticks.Count;
    public double? Bpm { get; private set; }
    public MetronomeProfile? Result { get; private set; }
    public bool IsDone => Status != LearnStatus.Learning;

    public void OnFrame(TimeSpan now, bool sustainedOpen, TimeSpan frameLength)
    {
        if (Status != LearnStatus.Learning) return;
        if (sustainedOpen) _sustained += frameLength;
        if (_sustained >= MaxSustained) Status = LearnStatus.TooMuchSound;
        else if (now - _start >= Timeout) Status = LearnStatus.Timeout;
    }

    public void OnCleanTick(CleanTick tick)
    {
        if (Status != LearnStatus.Learning || tick.Burst.Onset < _start) return;
        _ticks.Add(tick.Sample);
        if (_ticks.Count < RequiredTicks || tick.Burst.Onset - _start < MinDuration) return;

        var last = _ticks.TakeLast(RequiredTicks).ToList();
        var interval = TickClusterer.SteadyIntervalSeconds(last, 0.15);
        if (interval == null) return;
        var clusters = TickClusterer.Cluster(last);
        var kept = clusters.Where(c => c.Count >= 2).ToList();
        if (kept.Count == 0 || kept.Count > 3 || kept.Sum(c => c.Count) * 5 < last.Count * 4) return;

        Bpm = 60 / interval.Value;
        var profile = new MetronomeProfile { Locked = true, Clusters = kept, MinBpm = Bpm.Value, MaxBpm = Bpm.Value };
        var add = _bank.TryAdd(profile);
        Result = add.Profile;
        Status = add.Kind switch { AddKind.Added => LearnStatus.Success, AddKind.Merged when !add.PromotedTentative => LearnStatus.AlreadyKnown,
            AddKind.Merged => LearnStatus.Success, _ => LearnStatus.BankFull };
    }

    public void Cancel()
    {
        if (Status == LearnStatus.Learning) Status = LearnStatus.Cancelled;
    }
}
