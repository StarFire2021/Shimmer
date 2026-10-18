using Shimmer.Models;
using Shimmer.Services;
using Xunit;

namespace Shimmer.Tests;

public class MemberSorterTests
{
    private static readonly IReadOnlyDictionary<string, DateTime> NoUsage =
        new Dictionary<string, DateTime>();

    private static DesktopItem Item(string name, string path, bool dir = false, DateTime? modified = null)
        => new() { Name = name, Path = path, IsDirectory = dir, Modified = modified };

    [Fact]
    public void Manual_PreservesGivenOrder()
    {
        var items = new[] { Item("b", "b.txt"), Item("a", "a.txt"), Item("c", "c.txt") };
        var result = MemberSorter.Sort(items, SortMode.Manual, NoUsage);
        Assert.Equal(new[] { "b.txt", "a.txt", "c.txt" }, result.Select(i => i.Path));
    }

    [Fact]
    public void Name_IsAlphabeticalCaseInsensitive()
    {
        var items = new[] { Item("Banana", "2"), Item("apple", "1"), Item("Cherry", "3") };
        var result = MemberSorter.Sort(items, SortMode.Name, NoUsage);
        Assert.Equal(new[] { "apple", "Banana", "Cherry" }, result.Select(i => i.Name));
    }

    [Fact]
    public void Type_PutsFoldersFirstThenByExtension()
    {
        var items = new[]
        {
            Item("doc", "a.txt"),
            Item("folder", "zfolder", dir: true),
            Item("img", "b.png"),
            Item("doc2", "c.txt"),
        };
        var result = MemberSorter.Sort(items, SortMode.Type, NoUsage);

        Assert.Equal("zfolder", result[0].Path);            // folder first
        Assert.Equal("b.png", result[1].Path);              // .png before .txt
        Assert.Equal(new[] { "a.txt", "c.txt" }, result.Skip(2).Select(i => i.Path));
    }

    [Fact]
    public void DateModified_NewestFirst()
    {
        var items = new[]
        {
            Item("old", "old", modified: new DateTime(2020, 1, 1)),
            Item("new", "new", modified: new DateTime(2024, 1, 1)),
            Item("mid", "mid", modified: new DateTime(2022, 1, 1)),
        };
        var result = MemberSorter.Sort(items, SortMode.DateModified, NoUsage);
        Assert.Equal(new[] { "new", "mid", "old" }, result.Select(i => i.Path));
    }

    [Fact]
    public void RecentlyUsed_MostRecentFirst_UnusedGoLast()
    {
        var items = new[] { Item("a", "a"), Item("b", "b"), Item("c", "c") };
        var usage = new Dictionary<string, DateTime>
        {
            ["a"] = new DateTime(2024, 1, 1),
            ["b"] = new DateTime(2024, 6, 1),
            // "c" never used
        };
        var result = MemberSorter.Sort(items, SortMode.RecentlyUsed, usage);
        Assert.Equal(new[] { "b", "a", "c" }, result.Select(i => i.Path));
    }

    [Fact]
    public void Sort_DoesNotMutateInput()
    {
        var items = new List<DesktopItem> { Item("b", "b"), Item("a", "a") };
        _ = MemberSorter.Sort(items, SortMode.Name, NoUsage);
        Assert.Equal(new[] { "b", "a" }, items.Select(i => i.Path)); // original untouched
    }
}
