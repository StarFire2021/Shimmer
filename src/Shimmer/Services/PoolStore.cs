using System.IO;
using System.Text.Json;
using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Loads and saves the list of pools as JSON under
/// <c>%AppData%\Shimmer\pools.json</c>. Deliberately dumb: no migrations, no
/// locking. Saving is debounced by the caller so we are not hammering disk.
/// </summary>
public sealed class PoolStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public PoolStore()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Shimmer");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "pools.json");
    }

    public List<Pool> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Pool>();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Pool>>(json, Options) ?? new List<Pool>();
        }
        catch
        {
            // A corrupt file should not brick the app; start clean and let the
            // next save overwrite it.
            return new List<Pool>();
        }
    }

    public void Save(IEnumerable<Pool> pools)
    {
        string json = JsonSerializer.Serialize(pools, Options);
        // Write to a temp file then copy, so a crash mid-write cannot truncate
        // the real file.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}
