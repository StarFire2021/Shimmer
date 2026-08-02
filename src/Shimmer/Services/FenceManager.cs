using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Owns the set of fences and their membership (by file path) and persists them.
/// In the tile model this is pure bookkeeping — no desktop-icon interop — because
/// fences render our own tiles rather than moving Explorer's icons.
/// </summary>
public sealed class FenceManager
{
    private readonly FenceStore _store;
    public List<Fence> Fences { get; }

    public FenceManager(FenceStore store)
    {
        _store = store;
        Fences = store.Load();
    }

    public void Save() => _store.Save(Fences);

    public Fence CreateFence(double left, double top, double width, double height)
    {
        var fence = new Fence { Left = left, Top = top, Width = width, Height = height, Title = "New Fence" };
        Fences.Add(fence);
        Save();
        return fence;
    }

    public void RemoveFence(Fence fence)
    {
        Fences.Remove(fence);
        Save();
    }

    /// <summary>Move an item into a fence, removing it from any other fence first.</summary>
    public void Assign(string path, Fence fence)
    {
        foreach (var f in Fences)
            f.MemberPaths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (!fence.Contains(path))
            fence.MemberPaths.Add(path);
        Save();
    }

    /// <summary>Remove an item from whatever fence holds it (back to the loose desktop).</summary>
    public void RemoveFromFences(string path)
    {
        bool changed = false;
        foreach (var f in Fences)
            changed |= f.MemberPaths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (changed) Save();
    }

    public bool IsFenced(string path) => Fences.Any(f => f.Contains(path));
}
