namespace Shimmer.Models;

/// <summary>How a pool's member tiles are ordered.</summary>
public enum SortMode
{
    /// <summary>Custom order the user set by dragging (the raw MemberPaths order).</summary>
    Manual = 0,
    /// <summary>Alphabetical by display name.</summary>
    Name = 1,
    /// <summary>Folders first, then by file extension, then name.</summary>
    Type = 2,
    /// <summary>Most recently modified first.</summary>
    DateModified = 3,
    /// <summary>Most recently launched (through Shimmer) first.</summary>
    RecentlyUsed = 4,
}
