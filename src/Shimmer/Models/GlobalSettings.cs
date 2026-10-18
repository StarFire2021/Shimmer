namespace Shimmer.Models;

/// <summary>
/// App-wide defaults applied to every pool. When the user sets one of these from
/// the tray menu it is applied to all existing pools and used as the starting
/// value for any new pool.
/// </summary>
public sealed class GlobalSettings
{
    public SortMode SortMode { get; set; } = SortMode.Manual;
    public TitleAlign TitleAlign { get; set; } = TitleAlign.Left;
    public bool AutoSnap { get; set; }
    public bool MagneticSnap { get; set; }
    public bool ShowMenuButton { get; set; }
    public bool AutoHide { get; set; }

    /// <summary>Copy these defaults onto a pool.</summary>
    public void ApplyTo(Pool pool)
    {
        pool.SortMode = SortMode;
        pool.TitleAlign = TitleAlign;
        pool.AutoSnap = AutoSnap;
        pool.MagneticSnap = MagneticSnap;
        pool.ShowMenuButton = ShowMenuButton;
        pool.AutoHide = AutoHide;
    }
}
