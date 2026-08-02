using Shimmer.Models;

namespace Shimmer.Services;

/// <summary>
/// Owns the set of pools and their membership (by file path) and persists them.
/// In the tile model this is pure bookkeeping — no desktop-icon interop — because
/// pools render our own tiles rather than moving Explorer's icons.
/// </summary>
public sealed class PoolManager
{
    private readonly PoolStore _store;
    public List<Pool> Pools { get; }

    public PoolManager(PoolStore store)
    {
        _store = store;
        Pools = store.Load();
    }

    public void Save() => _store.Save(Pools);

    public Pool CreatePool(double left, double top, double width, double height)
    {
        var pool = new Pool { Left = left, Top = top, Width = width, Height = height, Title = "New Pool" };
        Pools.Add(pool);
        Save();
        return pool;
    }

    public void RemovePool(Pool pool)
    {
        Pools.Remove(pool);
        Save();
    }

    /// <summary>Move an item into a pool, removing it from any other pool first.</summary>
    public void Assign(string path, Pool pool)
    {
        foreach (var p in Pools)
            p.MemberPaths.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (!pool.Contains(path))
            pool.MemberPaths.Add(path);
        Save();
    }

    /// <summary>Remove an item from whatever pool holds it (back to the loose desktop).</summary>
    public void RemoveFromPools(string path)
    {
        bool changed = false;
        foreach (var p in Pools)
            changed |= p.MemberPaths.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (changed) Save();
    }

    public bool IsPooled(string path) => Pools.Any(p => p.Contains(path));
}
