using System.Windows.Media;

namespace Shimmer.Models;

/// <summary>
/// A real item that lives on the desktop (a shortcut, file, or folder), which
/// Shimmer now renders itself as a tile. Identified by its full filesystem path
/// — that is the stable key we use for fence membership and saved positions.
/// </summary>
public sealed class DesktopItem
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }

    /// <summary>The item's real Windows icon, as a WPF image (may be null if extraction failed).</summary>
    public ImageSource? Icon { get; set; }

    public override string ToString() => $"{Name} ({Path})";
}
