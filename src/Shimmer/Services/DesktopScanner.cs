using System.IO;
using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Enumerates what lives on the desktop. The desktop is really two folders — the
/// current user's Desktop and the shared (all-users) Desktop — so we scan both.
/// Each entry becomes a <see cref="DesktopItem"/> with its real icon and display
/// name pulled from the shell.
///
/// Phase 1 covers file-based items (shortcuts, files, folders). System namespace
/// icons like Recycle Bin and This PC are not files and aren't included yet;
/// that's a later phase.
/// </summary>
public static class DesktopScanner
{
    public static List<DesktopItem> Scan()
    {
        var items = new List<DesktopItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in DesktopFolders())
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch { continue; }

            foreach (var path in entries)
            {
                var fileName = Path.GetFileName(path);
                // Skip Windows' hidden desktop.ini bookkeeping file.
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(fileName)) continue; // de-dupe user vs public overlaps

                bool isDir = Directory.Exists(path);
                var item = new DesktopItem
                {
                    Path = path,
                    Name = IconExtractor.GetDisplayName(path),
                    IsDirectory = isDir,
                    Icon = IconExtractor.GetIcon(path),
                };
                items.Add(item);
            }
        }

        return items;
    }

    private static IEnumerable<string> DesktopFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }
}
