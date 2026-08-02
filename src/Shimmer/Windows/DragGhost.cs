using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Windows;

/// <summary>
/// A small translucent icon that follows the cursor while a tile is being
/// dragged, giving clear visual feedback while dragging. It's a topmost, click-through
/// layered window positioned in physical pixels (so no DPI math), updated from
/// the tile's drag-feedback events.
/// </summary>
public sealed class DragGhost : Window
{
    public DragGhost(ImageSource? icon)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        IsHitTestVisible = false;
        Width = 44;
        Height = 44;
        Opacity = 0.75;
        Content = new Image { Source = icon, Stretch = Stretch.Uniform };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    /// <summary>Position the ghost near the cursor (physical pixels).</summary>
    public void MoveTo(int screenX, int screenY)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, screenX + 14, screenY + 8, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
