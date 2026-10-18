using System.IO;
using System.Text.Json;
using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>Loads and saves the app-wide pool defaults to <c>%AppData%\Shimmer\global.json</c>.</summary>
public sealed class GlobalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public GlobalStore() : this(DefaultDir()) { }

    public GlobalStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "global.json");
    }

    private static string DefaultDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shimmer");

    public GlobalSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new GlobalSettings();
            return JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(_filePath), Options)
                   ?? new GlobalSettings();
        }
        catch
        {
            return new GlobalSettings();
        }
    }

    public void Save(GlobalSettings settings)
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options)); }
        catch { /* best effort */ }
    }
}
