using PracticeTimer.Core.Metronome;

namespace PracticeTimer.Core.Persistence;

public sealed class MetronomeStore
{
    public sealed class Data { public List<MetronomeProfile> Profiles { get; set; } = new(); }

    private readonly JsonStore<Data> _store;
    public MetronomeStore(string path) => _store = new JsonStore<Data>(path);

    public List<MetronomeProfile> Load() => _store.Load().Profiles;

    /// <summary>Only user-taught (locked) metronomes are persisted; auto-learned ones are session-only.</summary>
    public void Save(IEnumerable<MetronomeProfile> profiles) =>
        _store.Save(new Data { Profiles = profiles.Where(p => p.Locked).ToList() });

    public static bool LearnedOnDifferentMic(MetronomeProfile p, string? currentDeviceId) =>
        p.MicDeviceId != null && currentDeviceId != null && p.MicDeviceId != currentDeviceId;
}
