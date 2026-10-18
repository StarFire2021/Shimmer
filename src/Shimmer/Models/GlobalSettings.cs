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
    public string Color { get; set; } = "#3A6EA5";
    public double Opacity { get; set; } = 0.5;

    /// <summary>
    /// Copy these defaults onto a pool (used for new pools). Roll-up is
    /// intentionally excluded — it's a transient state, offered only as a bulk
    /// "roll all up/down" command, never a new-pool default.
    /// </summary>
    public void ApplyTo(Pool pool)
    {
        pool.SortMode = SortMode;
        pool.TitleAlign = TitleAlign;
        pool.AutoSnap = AutoSnap;
        pool.MagneticSnap = MagneticSnap;
        pool.ShowMenuButton = ShowMenuButton;
        pool.AutoHide = AutoHide;
        pool.Color = Color;
        pool.Opacity = Opacity;
    }
}
