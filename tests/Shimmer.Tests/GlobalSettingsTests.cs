using System.IO;
using Shimmer.Models;
using Shimmer.Services;
using Xunit;

namespace Shimmer.Tests;

public class GlobalSettingsTests : IDisposable
{
    private readonly string _dir;

    public GlobalSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ShimmerGlobal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void ApplyTo_CopiesEveryDefaultOntoThePool()
    {
        var g = new GlobalSettings
        {
            SortMode = SortMode.Name,
            TitleAlign = TitleAlign.Center,
            AutoSnap = true,
            MagneticSnap = false,
            ShowMenuButton = true,
            AutoHide = true,
        };
        var pool = new Pool();

        g.ApplyTo(pool);

        Assert.Equal(SortMode.Name, pool.SortMode);
        Assert.Equal(TitleAlign.Center, pool.TitleAlign);
        Assert.True(pool.AutoSnap);
        Assert.False(pool.MagneticSnap);
        Assert.True(pool.ShowMenuButton);
        Assert.True(pool.AutoHide);
    }

    [Fact]
    public void GlobalStore_PersistsAcrossReload()
    {
        var g = new GlobalSettings { SortMode = SortMode.Type, ShowMenuButton = true };
        new GlobalStore(_dir).Save(g);

        var loaded = new GlobalStore(_dir).Load();

        Assert.Equal(SortMode.Type, loaded.SortMode);
        Assert.True(loaded.ShowMenuButton);
    }

    [Fact]
    public void GlobalStore_MissingFile_ReturnsDefaults()
    {
        var loaded = new GlobalStore(_dir).Load();
        Assert.Equal(SortMode.Manual, loaded.SortMode);
        Assert.False(loaded.AutoSnap);
    }
}
