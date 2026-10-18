namespace Shimmer.Services;

/// <summary>
/// Pure geometry for "snap to icon grid" pool sizing: given a proposed width and
/// the number of tiles, work out a size that holds a whole grid of cells with
/// even padding on every side (columns from the width, rows to fit all tiles).
/// Kept free of UI so it can be unit-tested.
/// </summary>
public static class GridSizer
{
    public static (double Width, double Height) Snap(
        double proposedWidth, int tileCount,
        double cellW, double cellH, double bodyPad, double titleHeight)
    {
        int count = Math.Max(1, tileCount);
        int cols = Math.Max(1, (int)Math.Round((proposedWidth - bodyPad) / cellW));
        int rows = (int)Math.Ceiling(count / (double)cols);

        double width = cols * cellW + bodyPad;
        double height = titleHeight + rows * cellH + bodyPad;
        return (width, height);
    }

    /// <summary>
    /// Magnetic snap along one axis: if <paramref name="value"/> is within
    /// <paramref name="range"/> of a clean "N whole cells + offset" size, return
    /// that size (so the edge matches the opposite side's padding); otherwise
    /// return the value unchanged. Never snaps below one cell.
    /// </summary>
    public static double MagneticSnap(double value, double cell, double offset, double range)
    {
        double units = Math.Max(1, Math.Round((value - offset) / cell));
        double snapped = units * cell + offset;
        return Math.Abs(snapped - value) <= range ? snapped : value;
    }
}
