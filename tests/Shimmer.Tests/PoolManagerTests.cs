using System.IO;
using Shimmer.Models;
using Shimmer.Services;
using Xunit;

namespace Shimmer.Tests;

/// <summary>
/// Integration tests for pool membership + persistence (guards the behavior the
/// drag/drop and delete flows rely on). Uses a real PoolStore pointed at a temp
/// directory, so it also exercises the JSON round-trip.
/// </summary>
public class PoolManagerTests : IDisposable
{
    private readonly string _dir;

    public PoolManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ShimmerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private PoolManager NewManager() => new(new PoolStore(_dir));

    [Fact]
    public void CreatePool_AddsAndPersists()
    {
        var m = NewManager();
        var p = m.CreatePool(10, 20, 300, 200);
        Assert.Single(m.Pools);

        var reloaded = NewManager();
        Assert.Single(reloaded.Pools);
        Assert.Equal(p.Id, reloaded.Pools[0].Id);
    }

    [Fact]
    public void Assign_AddsPath_CaseInsensitiveMembership()
    {
        var m = NewManager();
        var p = m.CreatePool(0, 0, 1, 1);

        m.Assign(@"C:\x\a.txt", p);

        Assert.True(p.Contains(@"C:\x\a.txt"));
        Assert.True(m.IsPooled(@"c:\x\a.txt")); // case-insensitive
    }

    [Fact]
    public void Assign_MovesItemBetweenPools()
    {
        var m = NewManager();
        var a = m.CreatePool(0, 0, 1, 1);
        var b = m.CreatePool(0, 0, 1, 1);

        m.Assign("item", a);
        m.Assign("item", b);

        Assert.False(a.Contains("item"));
        Assert.True(b.Contains("item"));
    }

    [Fact]
    public void RemoveFromPools_RemovesEverywhere()
    {
        var m = NewManager();
        var a = m.CreatePool(0, 0, 1, 1);
        m.Assign("item", a);

        m.RemoveFromPools("item");

        Assert.False(a.Contains("item"));
        Assert.False(m.IsPooled("item"));
    }

    [Fact]
    public void RemovePool_DropsThePool()
    {
        var m = NewManager();
        var p = m.CreatePool(0, 0, 1, 1);
        m.RemovePool(p);
        Assert.Empty(m.Pools);
    }

    [Fact]
    public void Membership_Order_And_SortMode_Persist()
    {
        var m = NewManager();
        var p = m.CreatePool(0, 0, 1, 1);
        m.Assign("a", p);
        m.Assign("b", p);
        m.Assign("c", p);
        p.SortMode = SortMode.Name;
        m.Save();

        var reloaded = NewManager();
        var rp = reloaded.Pools[0];
        Assert.Equal(new[] { "a", "b", "c" }, rp.MemberPaths); // manual order preserved
        Assert.Equal(SortMode.Name, rp.SortMode);
    }
}
