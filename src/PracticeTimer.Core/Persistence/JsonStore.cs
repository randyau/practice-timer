using System.Text.Json;
using System.Text.Json.Serialization;

namespace PracticeTimer.Core.Persistence;

/// <summary>
/// Versioned, atomically written JSON file. Content that can't be used is set aside under a new name, never
/// deleted or overwritten blindly, and loading never throws.
/// <list type="bullet">
/// <item>Older version: <paramref name="migrate"/> converts it when given; otherwise the data is read as-is, which suits additive changes.</item>
/// <item>Newer version (written by a later build): set aside untouched, defaults returned.</item>
/// <item>Corrupt: set aside, defaults returned.</item>
/// <item>Read blocked after retries (another process has it open): defaults returned and a copy is kept, so the original is never lost.</item>
/// </list>
/// </summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private const int ReadAttempts = 3;
    private readonly string _path;
    private readonly int _version;
    private readonly Func<int, JsonElement, T?>? _migrate;

    public JsonStore(string path, int version = 1, Func<int, JsonElement, T?>? migrate = null)
    {
        _path = path;
        _version = version;
        _migrate = migrate;
    }

    private sealed class Envelope
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("data")] public JsonElement Data { get; set; }
    }

    public T Load()
    {
        if (!File.Exists(_path)) return new T();
        string text;
        try { text = ReadWithRetry(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            KeepCopy();
            return new T();
        }

        try
        {
            var env = JsonSerializer.Deserialize<Envelope>(text, Options);
            if (env is { Data.ValueKind: JsonValueKind.Object })
            {
                if (env.Version == _version) return env.Data.Deserialize<T>(Options) ?? throw new JsonException("empty data");
                if (env.Version < _version)
                    return (_migrate != null ? _migrate(env.Version, env.Data) : env.Data.Deserialize<T>(Options)) ?? throw new JsonException("empty data");
                SetAside($"newer-v{env.Version}");
                return new T();
            }
        }
        catch (JsonException) { }
        SetAside("corrupt");
        return new T();
    }

    public void Save(T value)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(new Envelope { Version = _version, Data = JsonSerializer.SerializeToElement(value, Options) }, Options);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
            w.Write(json);
            w.Flush();
            fs.Flush(flushToDisk: true); // on disk before the swap, so a power cut can't leave an empty file
        }
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }

    private string ReadWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(_path); }
            catch (IOException) when (attempt < ReadAttempts) { Thread.Sleep(50 * attempt); }
        }
    }

    private string AsideName(string reason)
    {
        var name = Path.GetFileNameWithoutExtension(_path);
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        return Path.Combine(Path.GetDirectoryName(_path) ?? "", $"{name}.{reason}-{stamp}.json");
    }

    private void SetAside(string reason)
    {
        try { File.Move(_path, AsideName(reason)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void KeepCopy()
    {
        try { File.Copy(_path, AsideName("unreadable")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
