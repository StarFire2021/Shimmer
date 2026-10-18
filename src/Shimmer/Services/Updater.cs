using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shimmer.Services;

/// <summary>Details of an available release.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string InstallerUrl, string HtmlUrl);

/// <summary>
/// Checks GitHub Releases for a newer version and downloads its installer. The
/// repo is public, so this is an unauthenticated call to the GitHub REST API.
/// </summary>
public static class Updater
{
    private const string Owner = "StarFire2021";
    private const string Repo = "Shimmer";
    private const string LatestApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub requires a User-Agent.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Shimmer-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>True if <paramref name="other"/> is a newer release than what's running.</summary>
    public static bool IsNewer(Version other)
    {
        int CurBuild() => Math.Max(CurrentVersion.Build, 0);
        int OthBuild() => Math.Max(other.Build, 0);
        if (other.Major != CurrentVersion.Major) return other.Major > CurrentVersion.Major;
        if (other.Minor != CurrentVersion.Minor) return other.Minor > CurrentVersion.Minor;
        return OthBuild() > CurBuild();
    }

    /// <summary>Query the latest release. Returns null if it can't be read or has no installer.</summary>
    public static async Task<UpdateInfo?> GetLatestAsync()
    {
        var json = await Http.GetStringAsync(LatestApi);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var version = ParseVersion(tag);
        if (version is null) return null;

        string? installer = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installer = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
        }
        if (installer is null) return null;

        return new UpdateInfo(version, tag, installer, htmlUrl);
    }

    /// <summary>Download the installer to a temp file and return its path.</summary>
    public static async Task<string> DownloadInstallerAsync(string url)
    {
        var bytes = await Http.GetByteArrayAsync(url);
        var path = Path.Combine(Path.GetTempPath(), "Shimmer-Setup-Update.exe");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static Version? ParseVersion(string tag)
    {
        tag = tag.TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? v : null;
    }
}
