using Shimmer.Services;
using Xunit;

namespace Shimmer.Tests;

public class GridSizerTests
{
    // Matches PoolWindow's constants.
    private const double CellW = 92, CellH = 96, Pad = 12, Title = 34;

    private static (double W, double H) Snap(double width, int count)
        => GridSizer.Snap(width, count, CellW, CellH, Pad, Title);

    [Fact]
    public void SingleTile_IsOneCell()
    {
        var (w, h) = Snap(CellW + Pad, 1);
        Assert.Equal(CellW + Pad, w);             // 104
        Assert.Equal(Title + CellH + Pad, h);     // 142
    }

    [Fact]
    public void WidthChoosesColumns_RowsFitAllTiles()
    {
        // ~2 columns wide, 3 tiles -> 2 cols x 2 rows
        var (w, h) = Snap(2 * CellW + Pad, 3);
        Assert.Equal(2 * CellW + Pad, w);         // 196
        Assert.Equal(Title + 2 * CellH + Pad, h); // 238
    }

    [Fact]
    public void WiderPool_UsesMoreColumns_FewerRows()
    {
        var (w, h) = Snap(4 * CellW + Pad, 4);    // 4 cols, 1 row
        Assert.Equal(4 * CellW + Pad, w);
        Assert.Equal(Title + CellH + Pad, h);
    }

    [Fact]
    public void ClampsToAtLeastOneColumn()
    {
        var (w, h) = Snap(10, 5);                 // absurdly narrow -> 1 col, 5 rows
        Assert.Equal(CellW + Pad, w);
        Assert.Equal(Title + 5 * CellH + Pad, h);
    }

    [Fact]
    public void EmptyPool_SizesAsOneCell()
    {
        var (w, h) = Snap(CellW + Pad, 0);        // count 0 treated as 1
        Assert.Equal(CellW + Pad, w);
        Assert.Equal(Title + CellH + Pad, h);
    }

    [Fact]
    public void Dimensions_AreAlwaysWholeCellsPlusPadding()
    {
        // Any arbitrary width snaps so (w - pad) and (h - title - pad) are whole cells.
        var (w, h) = Snap(237, 7);
        Assert.Equal(0, (w - Pad) % CellW);
        Assert.Equal(0, (h - Title - Pad) % CellH);
    }

    // ---- MagneticSnap ----------------------------------------------------
    // Clean 2-column width = 2*92 + 12 = 196.

    [Fact]
    public void Magnetic_WithinRange_SnapsToEvenPadding()
    {
        Assert.Equal(196, GridSizer.MagneticSnap(190, CellW, Pad, 22)); // 6 px away -> snaps
        Assert.Equal(196, GridSizer.MagneticSnap(205, CellW, Pad, 22)); // 9 px away -> snaps
    }

    [Fact]
    public void Magnetic_OutsideRange_StaysFree()
    {
        Assert.Equal(230, GridSizer.MagneticSnap(230, CellW, Pad, 22)); // 34 px from 196 -> unchanged
    }

    [Fact]
    public void Magnetic_SnapsToNearestBoundary()
    {
        Assert.Equal(288, GridSizer.MagneticSnap(284, CellW, Pad, 22)); // near 3-col (3*92+12)
    }

    [Fact]
    public void Magnetic_NeverSnapsBelowOneCell()
    {
        Assert.Equal(104, GridSizer.MagneticSnap(110, CellW, Pad, 22)); // snaps to one cell (92+12)
    }
}
