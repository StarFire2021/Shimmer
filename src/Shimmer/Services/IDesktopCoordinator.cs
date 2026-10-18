using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// The contract the loose layer, pool windows, and tiles use to talk back to the
/// app "brain" (implemented by App). It resolves which items are loose vs pooled,
/// applies drops, and toggles the loose layer's drop-hittability during a drag.
/// </summary>
public interface IDesktopCoordinator
{
    /// <summary>Items not currently in any pool.</summary>
    IEnumerable<DesktopItem> LooseItems();

    /// <summary>Whether loose (un-pooled) tiles show their square background (global default).</summary>
    bool LooseTileBackground { get; }

    /// <summary>Items belonging to a given pool (in membership order).</summary>
    IEnumerable<DesktopItem> MembersOf(Pool pool);

    /// <summary>
    /// Assign an item to a pool at a specific position (removing it from any other
    /// pool / loose). Switches the pool to Manual order so the arrangement sticks.
    /// </summary>
    void DropToPoolAt(string path, Pool pool, int index);

    /// <summary>Release an item back onto the desktop at the given loose-layer coordinates.</summary>
    void DropToLoose(string path, double canvasX, double canvasY);

    /// <summary>Record that an item was just launched (for the "Recently used" sort).</summary>
    void RecordLaunch(string path);

    /// <summary>Called when a tile drag starts, so the loose layer can become a drop target.</summary>
    void BeginDrag();

    /// <summary>Called when a tile drag ends, reverting the loose layer to click-through.</summary>
    void EndDrag();

    /// <summary>Delete a pool (its items return to the loose desktop) and close its window.</summary>
    void DeletePool(Pool pool);
}

/// <summary>Clipboard/drag data format for moving a desktop item between hosts.</summary>
public static class DragFormats
{
    public const string Item = "ShimmerItemPath";
}
