using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// The contract the loose layer, fence windows, and tiles use to talk back to the
/// app "brain" (implemented by App). It resolves which items are loose vs fenced,
/// applies drops, and toggles the loose layer's drop-hittability during a drag.
/// </summary>
public interface IDesktopCoordinator
{
    /// <summary>Items not currently in any fence.</summary>
    IEnumerable<DesktopItem> LooseItems();

    /// <summary>Items belonging to a given fence (in membership order).</summary>
    IEnumerable<DesktopItem> MembersOf(Fence fence);

    /// <summary>Assign an item to a fence (removing it from any other fence / loose).</summary>
    void DropToFence(string path, Fence fence);

    /// <summary>Release an item back onto the desktop at the given loose-layer coordinates.</summary>
    void DropToLoose(string path, double canvasX, double canvasY);

    /// <summary>Called when a tile drag starts, so the loose layer can become a drop target.</summary>
    void BeginDrag();

    /// <summary>Called when a tile drag ends, reverting the loose layer to click-through.</summary>
    void EndDrag();

    /// <summary>Delete a fence (its items return to the loose desktop) and close its window.</summary>
    void DeleteFence(Fence fence);
}

/// <summary>Clipboard/drag data format for moving a desktop item between hosts.</summary>
public static class DragFormats
{
    public const string Item = "ShimmerItemPath";
}
