using System.IO;
using System.Text.Json;

namespace Shimmer.Services;

/// <summary>
/// Records when each item was last launched through Shimmer (by file path), to
/// drive the "Recently used" sort. Persisted to <c>usage.json</c>.
/// </summary>
public sealed class UsageStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Dictionary<string, DateTime> _usage;

    public UsageStore() : this(DefaultDir()) { }

    /// <summary>Overridable directory, used by tests.</summary>
    public UsageStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "usage.json");
        _usage = Load();
    }

    private static string DefaultDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shimmer");

    private Dictionary<string, DateTime> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json, Options);
            return data is null ? new(StringComparer.OrdinalIgnoreCase)
                                : new(data, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Record(string path, DateTime when)
    {
        _usage[path] = when;
        Save();
    }

    public void Record(string path) => Record(path, DateTime.Now);

    public DateTime? Get(string path) => _usage.TryGetValue(path, out var t) ? t : null;

    /// <summary>A read-only view for sorting.</summary>
    public IReadOnlyDictionary<string, DateTime> Snapshot() => _usage;

    private void Save()
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_usage, Options)); }
        catch { /* best effort */ }
    }
}
