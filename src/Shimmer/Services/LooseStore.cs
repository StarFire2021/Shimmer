using System.IO;
using System.Text.Json;

namespace Shimmer.Services;

/// <summary>Saved on-desktop position for a loose tile (device-independent units).</summary>
public record struct LoosePos(double X, double Y);

/// <summary>
/// Remembers where the user has dragged each loose desktop tile, keyed by the
/// item's full path, in <c>%AppData%\Shimmer\loose.json</c>. Items without a
/// saved position get auto-arranged into a grid on first run.
/// </summary>
public sealed class LooseStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private Dictionary<string, LoosePos> _positions;

    public LooseStore()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shimmer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "loose.json");
        _positions = Load();
    }

    private Dictionary<string, LoosePos> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, LoosePos>>(json, Options) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public bool TryGet(string path, out LoosePos pos) => _positions.TryGetValue(path, out pos);

    public void Set(string path, double x, double y)
    {
        _positions[path] = new LoosePos(x, y);
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_positions, Options));
        }
        catch
        {
            // Non-fatal: a failed position save just means tiles re-grid next launch.
        }
    }
}
