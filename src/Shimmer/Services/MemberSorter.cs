using System.IO;
using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Pure ordering of a pool's members by <see cref="SortMode"/>. Kept free of UI
/// and I/O (metadata and usage times are passed in) so it is straightforward to
/// unit-test. Manual mode returns the items exactly as given (the caller supplies
/// them in the pool's MemberPaths order).
/// </summary>
public static class MemberSorter
{
    public static IReadOnlyList<DesktopItem> Sort(
        IReadOnlyList<DesktopItem> items,
        SortMode mode,
        IReadOnlyDictionary<string, DateTime> usage)
    {
        var oic = StringComparer.OrdinalIgnoreCase;
        return mode switch
        {
            SortMode.Name =>
                items.OrderBy(i => i.Name, oic).ToList(),

            SortMode.Type =>
                items.OrderBy(i => i.IsDirectory ? 0 : 1)
                     .ThenBy(i => Path.GetExtension(i.Path) ?? string.Empty, oic)
                     .ThenBy(i => i.Name, oic)
                     .ToList(),

            SortMode.DateModified =>
                items.OrderByDescending(i => i.Modified ?? DateTime.MinValue)
                     .ThenBy(i => i.Name, oic)
                     .ToList(),

            SortMode.RecentlyUsed =>
                items.OrderByDescending(i => usage.TryGetValue(i.Path, out var t) ? t : DateTime.MinValue)
                     .ThenBy(i => i.Name, oic)
                     .ToList(),

            _ => items.ToList(), // Manual: preserve given order
        };
    }
}
