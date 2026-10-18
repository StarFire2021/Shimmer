using System.IO;
using Shimmer.Services;
using Xunit;

namespace Shimmer.Tests;

public class UsageStoreTests : IDisposable
{
    private readonly string _dir;

    public UsageStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ShimmerUsage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Record_ThenGet_ReturnsTime()
    {
        var store = new UsageStore(_dir);
        var when = new DateTime(2024, 5, 1, 12, 0, 0);
        store.Record("item-x", when);
        Assert.Equal(when, store.Get("item-x"));
    }

    [Fact]
    public void Usage_PersistsAcrossReload()
    {
        var when = new DateTime(2024, 5, 1, 12, 0, 0);
        new UsageStore(_dir).Record("item-x", when);

        var reloaded = new UsageStore(_dir);
        Assert.Equal(when, reloaded.Get("item-x"));
    }

    [Fact]
    public void Get_UnknownPath_IsNull()
    {
        var store = new UsageStore(_dir);
        Assert.Null(store.Get("never-launched"));
    }
}
