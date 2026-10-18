using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Shimmer.Controls;
using Shimmer.Models;
using Shimmer.Services;
using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Windows;

/// <summary>
/// The desktop-wide layer that renders un-pooled items as tiles now that
/// Explorer's real icons are hidden. Spans the whole virtual screen, sits at the
/// bottom of the z-order, and is transparent (click-through) between tiles so the
/// desktop right-click menu still works.
///
/// Tiles are the shared <see cref="DesktopTile"/>. Dragging a tile onto a pool
/// moves it there; dropping a tile back here repositions it (or releases it from
/// a pool). Because the empty canvas is normally click-through, we make it a
/// solid (invisible) drop target only for the duration of a drag.
/// </summary>
public partial class LooseLayerWindow : Window
{
    private const int Cell = 96;
    private const double Margin_ = 16;

    private readonly IDesktopCoordinator _coord;
    private readonly LooseStore _store;

    public LooseLayerWindow(IDesktopCoordinator coord, LooseStore store)
    {
        InitializeComponent();
        _coord = coord;
        _store = store;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Root.AllowDrop = true;
        Root.DragOver += Root_DragOver;
        Root.Drop += Root_Drop;

        Loaded += (_, _) => Rebuild();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        SinkToBottom();
    }

    // Force ourselves to the very bottom whenever the z-order is touched, so the
    // loose layer stays underneath pools and all app windows.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = HWND_BOTTOM;
            wp.flags &= ~SWP_NOZORDER;
            Marshal.StructureToPtr(wp, lParam, false);
        }
        return IntPtr.Zero;
    }

    public void SinkToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Make the empty canvas hittable (drop target) during a drag, else click-through.</summary>
    public void SetDropMode(bool on)
    {
        Root.Background = on ? Brushes.Transparent : null;
    }

    /// <summary>Rebuild loose tiles from the coordinator's current loose set.</summary>
    public void Rebuild()
    {
        Root.Children.Clear();

        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        var work = SystemParameters.WorkArea; // primary monitor, minus taskbar (DIP)
        double originX = work.Left - vsLeft + Margin_;
        double originY = work.Top - vsTop + Margin_;
        int rows = Math.Max(1, (int)((work.Height - 2 * Margin_) / Cell));
        int gridIndex = 0;

        foreach (var item in _coord.LooseItems())
        {
            var tile = new DesktopTile(item, _coord, _coord.LooseTileBackground);

            if (_store.TryGet(item.Path, out var pos))
            {
                Canvas.SetLeft(tile, pos.X);
                Canvas.SetTop(tile, pos.Y);
            }
            else
            {
                int col = gridIndex / rows;
                int row = gridIndex % rows;
                Canvas.SetLeft(tile, originX + col * Cell);
                Canvas.SetTop(tile, originY + row * Cell);
                gridIndex++;
            }

            Root.Children.Add(tile);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormats.Item) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormats.Item) && e.Data.GetData(DragFormats.Item) is string path)
        {
            var p = e.GetPosition(Root);
            _coord.DropToLoose(path, p.X, p.Y);
            e.Handled = true;
        }
    }
}
