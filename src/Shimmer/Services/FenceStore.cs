using System.IO;
using System.Text.Json;
using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Loads and saves the list of fences as JSON under
/// <c>%AppData%\Shimmer\fences.json</c>. Deliberately dumb: no migrations, no
/// locking. Saving is debounced by the caller (FenceManager) so we are not
/// hammering disk on every icon nudge.
/// </summary>
public sealed class FenceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public FenceStore()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Shimmer");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "fences.json");
    }

    public List<Fence> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Fence>();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Fence>>(json, Options) ?? new List<Fence>();
        }
        catch
        {
            // A corrupt file should not brick the app; start clean and let the
            // next save overwrite it.
            return new List<Fence>();
        }
    }

    public void Save(IEnumerable<Fence> fences)
    {
        string json = JsonSerializer.Serialize(fences, Options);
        // Write to a temp file then move, so a crash mid-write cannot truncate
        // the real file.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}
