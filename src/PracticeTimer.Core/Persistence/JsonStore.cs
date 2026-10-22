using System.Text.Json;

namespace PracticeTimer.Core.Persistence;

/// <summary>Versioned, atomically written JSON file. Unreadable files are set aside, never fatal.</summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly int _version;

    public JsonStore(string path, int version = 1)
    {
        _path = path;
        _version = version;
    }

    private sealed class Envelope { public int Version { get; set; } public T? Data { get; set; } }

    public T Load()
    {
        if (!File.Exists(_path)) return new T();
        try
        {
            var env = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(_path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (env is { Data: not null } && env.Version == _version) return env.Data;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        SetAside();
        return new T();
    }

    public void Save(T value)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(new Envelope { Version = _version, Data = value }, Options)
            .Replace("\"Version\"", "\"version\"").Replace("\"Data\"", "\"data\"");
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private void SetAside()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(_path);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Move(_path, Path.Combine(Path.GetDirectoryName(_path) ?? "", $"{name}.corrupt-{stamp}.json"));
        }
        catch (IOException) { }
    }
}
