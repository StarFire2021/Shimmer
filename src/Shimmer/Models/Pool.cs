namespace Shimmer.Models;

/// <summary>
/// A pool: a labeled, tinted container that holds desktop items (by file path)
/// and renders them as tiles. In the tile model a pool owns its items outright —
/// there are no real desktop icons to fight — so membership is just a list of
/// paths plus the window's geometry and appearance.
/// </summary>
public sealed class Pool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Pool";

    // Window geometry, in WPF device-independent units.
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 240;

    // Appearance.
    public string Color { get; set; } = "#3A6EA5";
    public double Opacity { get; set; } = 0.5;

    /// <summary>When true, the whole pool hides until the cursor hovers its area.</summary>
    public bool AutoHide { get; set; }

    /// <summary>When true, the body is collapsed to just the title bar.</summary>
    public bool RolledUp { get; set; }

    /// <summary>Full paths of the desktop items that belong to this pool.</summary>
    public List<string> MemberPaths { get; set; } = new();

    public bool Contains(string path) =>
        MemberPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
}
