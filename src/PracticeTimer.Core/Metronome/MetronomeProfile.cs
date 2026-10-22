using System.Text.Json.Serialization;

namespace PracticeTimer.Core.Metronome;

public sealed class MetronomeCluster
{
    public double[] Spectrum { get; set; } = Array.Empty<double>();
    public double MeanPeakDb { get; set; }
    public int Count { get; set; }
}

public sealed class MetronomeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Metronome";
    public bool Locked { get; set; }
    public bool Enabled { get; set; } = true;
    public List<MetronomeCluster> Clusters { get; set; } = new();
    public double MinBpm { get; set; }
    public double MaxBpm { get; set; }
    public string? MicDeviceId { get; set; }
    public string? MicName { get; set; }
    /// <summary>Upper band edge the fingerprints were taken with; 0 in files written before this was recorded.</summary>
    public double BandHiHz { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public double EffectiveBandHiHz => BandHiHz > 0 ? BandHiHz : Analysis.BandSpectrum.DefaultHiHz;

    public MetronomeProfile Clone() => new()
    {
        Id = Id, Name = Name, Locked = Locked, Enabled = Enabled, MinBpm = MinBpm, MaxBpm = MaxBpm,
        MicDeviceId = MicDeviceId, MicName = MicName, BandHiHz = BandHiHz, CreatedUtc = CreatedUtc,
        LastMatched = LastMatched, SustainedMatches = SustainedMatches,
        Clusters = Clusters.Select(c => new MetronomeCluster { Spectrum = (double[])c.Spectrum.Clone(), MeanPeakDb = c.MeanPeakDb, Count = c.Count }).ToList(),
    };

    [JsonIgnore] public long LastMatched { get; set; }
    [JsonIgnore] public int SustainedMatches { get; set; }
}

public readonly record struct TickSample(TimeSpan Onset, double[] Spectrum, double PeakDb, TimeSpan Duration);

/// <summary>A short burst with quiet on both sides, suitable for learning from.</summary>
public sealed record CleanTick(Detection.Burst Burst, bool Matched)
{
    public TickSample Sample => new(Burst.Onset, Burst.Spectrum, Burst.PeakDb, Burst.Duration);
}
