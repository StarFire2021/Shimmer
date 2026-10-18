using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Shimmer.Controls;
using Shimmer.Models;
using Shimmer.Services;
using static Shimmer.Interop.NativeMethods;
using WinFormsCursor = System.Windows.Forms.Cursor;

namespace Shimmer.Windows;

/// <summary>
/// A pool: a tinted container window that renders its member items as tiles.
/// Supports drag-in / drag-out (drops), resize, color/opacity, roll-up, and
/// whole-pool hide-until-hover (the entire window vanishes until the cursor
/// enters its area).
/// </summary>
public partial class PoolWindow : Window
{
    private const double TitleHeight = 34;

    // Tile cell footprint (DesktopTile 84x88 + 4px margin all round) and the
    // ScrollViewer padding (6px each side), used for snap-to-grid sizing.
    private const double CellW = 92;
    private const double CellH = 96;
    private const double BodyPad = 12;
    private const double SnapRange = 22; // how close (px) an edge must be to snap magnetically

    // Minimum is exactly one icon cell, so a pool can be a single column/row.
    private const double MinWidth_ = CellW + BodyPad;              // 104
    private const double MinHeight_ = TitleHeight + CellH + BodyPad; // 142

    private bool _menuOpen;

    private readonly Pool _pool;
    private readonly PoolManager _manager;
    private readonly IDesktopCoordinator _coord;
    private readonly IntPtr _looseHwnd;
    private readonly DispatcherTimer _hoverTimer;
    private Rect _screenRectPx;

    public Pool Pool => _pool;

    public PoolWindow(Pool pool, PoolManager manager, IDesktopCoordinator coord, IntPtr looseHwnd)
    {
        InitializeComponent();
        _pool = pool;
        _manager = manager;
        _coord = coord;
        _looseHwnd = looseHwnd;

        Left = pool.Left;
        Top = pool.Top;
        Width = pool.Width;
        Height = pool.Height;
        TitleText.Text = pool.Title;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _hoverTimer.Tick += HoverTimer_Tick;

        Loaded += PoolWindow_Loaded;
    }

    // ---- Plumbing ---------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // Keep the pool pinned directly above the loose layer (and thus below all
    // real app windows) whenever the z-order changes.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = _looseHwnd != IntPtr.Zero ? _looseHwnd : HWND_BOTTOM;
            wp.flags &= ~SWP_NOZORDER;
            Marshal.StructureToPtr(wp, lParam, false);
        }
        return IntPtr.Zero;
    }

    private void PoolWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        Rebuild();
        AutoHideMenuItem.IsChecked = _pool.AutoHide;
        AutoSnapMenuItem.IsChecked = _pool.AutoSnap;
        MagneticSnapMenuItem.IsChecked = _pool.MagneticSnap;
        ShowMenuButtonMenuItem.IsChecked = _pool.ShowMenuButton;
        MenuButton.Visibility = _pool.ShowMenuButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleAlign();
        UpdateTitleAlignChecks();
        ApplyRolled();
        UpdateSortChecks();
        UpdateScreenRect();
        if (_pool.AutoHide)
        {
            _hoverTimer.Start();
            if (!CursorInside()) Visibility = Visibility.Hidden;
        }
    }

    public void SinkToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Rebuild the member tiles from current membership.</summary>
    public void Rebuild()
    {
        Tiles.Children.Clear();
        foreach (var item in _coord.MembersOf(_pool))
            Tiles.Children.Add(new DesktopTile(item, _coord));

        if (_pool.AutoSnap) ApplyAutoSnap(_pool.Width);
    }

    private void ApplyAppearance()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_pool.Color);
            byte a = (byte)(Math.Clamp(_pool.Opacity, 0, 1) * 255);
            Body.Background = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            TitleBar.Background = new SolidColorBrush(c);
        }
        catch { /* invalid hex: leave defaults */ }
    }

    private void UpdateScreenRect()
    {
        if (!IsVisible) return;
        try
        {
            var tl = PointToScreen(new Point(0, 0));
            var br = PointToScreen(new Point(ActualWidth, ActualHeight));
            _screenRectPx = new Rect(tl, br);
        }
        catch { /* not sourced yet */ }
    }

    private bool CursorInside()
    {
        var p = WinFormsCursor.Position; // physical pixels
        return _screenRectPx.Contains(p.X, p.Y);
    }

    // ---- Roll up ----------------------------------------------------------

    private void Roll_Click(object sender, RoutedEventArgs e)
    {
        _pool.RolledUp = !_pool.RolledUp;
        ApplyRolled();
        _manager.Save();
    }

    /// <summary>Collapse to just the title bar (rolled up) or restore the body.</summary>
    private void ApplyRolled()
    {
        BodyHost.Visibility = _pool.RolledUp ? Visibility.Collapsed : Visibility.Visible;
        Height = _pool.RolledUp ? TitleHeight : _pool.Height;
        RollMenuItem.IsChecked = _pool.RolledUp;
        RollButton.Content = _pool.RolledUp ? "▸" : "▾";
        UpdateScreenRect();
    }

    // ---- Sorting ----------------------------------------------------------

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag && Enum.TryParse<SortMode>(tag, out var mode))
        {
            _pool.SortMode = mode;
            _manager.Save();
            Rebuild();
            UpdateSortChecks();
        }
    }

    /// <summary>Tick the one sort item matching the pool's current mode.</summary>
    private void UpdateSortChecks()
    {
        foreach (var obj in SortMenu.Items)
            if (obj is MenuItem mi && mi.Tag is string tag && Enum.TryParse<SortMode>(tag, out var m))
                mi.IsChecked = m == _pool.SortMode;
    }

    // ---- Title alignment + menu button -----------------------------------

    private void TitleAlign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag && Enum.TryParse<TitleAlign>(tag, out var a))
        {
            _pool.TitleAlign = a;
            UpdateTitleAlign();
            UpdateTitleAlignChecks();
            _manager.Save();
        }
    }

    private void UpdateTitleAlign()
    {
        TitleText.TextAlignment = _pool.TitleAlign switch
        {
            TitleAlign.Center => TextAlignment.Center,
            TitleAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    private void UpdateTitleAlignChecks()
    {
        foreach (var obj in TitleAlignMenu.Items)
            if (obj is MenuItem mi && mi.Tag is string tag && Enum.TryParse<TitleAlign>(tag, out var a))
                mi.IsChecked = a == _pool.TitleAlign;
    }

    private void ShowMenuButton_Click(object sender, RoutedEventArgs e)
    {
        _pool.ShowMenuButton = !_pool.ShowMenuButton;
        ShowMenuButtonMenuItem.IsChecked = _pool.ShowMenuButton;
        MenuButton.Visibility = _pool.ShowMenuButton ? Visibility.Visible : Visibility.Collapsed;
        _manager.Save();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = TitleBar.ContextMenu;
        if (menu is null) return;
        menu.PlacementTarget = MenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- Hide until hover -------------------------------------------------

    private void AutoHide_Click(object sender, RoutedEventArgs e)
    {
        _pool.AutoHide = !_pool.AutoHide;
        AutoHideMenuItem.IsChecked = _pool.AutoHide;
        _manager.Save();

        if (_pool.AutoHide)
        {
            UpdateScreenRect();
            _hoverTimer.Start();
            if (!CursorInside()) Visibility = Visibility.Hidden;
        }
        else
        {
            _hoverTimer.Stop();
            Visibility = Visibility.Visible;
        }
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        if (!_pool.AutoHide) { _hoverTimer.Stop(); return; }

        // Stay visible while the context menu is open, even though navigating into
        // a submenu moves the cursor off the pool's rectangle.
        if (CursorInside() || _menuOpen)
        {
            if (Visibility != Visibility.Visible)
            {
                // Visibility.Hidden preserves z-order, so no re-sink needed — the
                // pool returns exactly where it was (above the loose layer).
                Visibility = Visibility.Visible;
                UpdateScreenRect();
            }
        }
        else if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Hidden;
        }
    }

    // While the right-click menu is open we (a) pause auto-hide and (b) let the
    // pool take focus. Normally the pool is a no-activate window, which prevents
    // nested submenu popups from holding focus and makes them dismiss while you
    // navigate to them. We drop that style for the duration of the menu and
    // foreground the window, then restore it on close.
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        _menuOpen = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);
        SetForegroundWindow(hwnd);
    }

    private void Menu_Closed(object sender, RoutedEventArgs e)
    {
        _menuOpen = false;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE); // restore no-activate
    }

    // ---- Drag to move -----------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        _pool.Left = Left;
        _pool.Top = Top;
        UpdateScreenRect();
        _manager.Save();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _pool.Left = Left;
        _pool.Top = Top;
        if (IsLoaded) UpdateScreenRect();
    }

    // ---- Drops ------------------------------------------------------------

    private void Body_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormats.Item) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Body_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormats.Item) && e.Data.GetData(DragFormats.Item) is string path)
        {
            int index = ComputeDropIndex(e.GetPosition(Tiles));
            _coord.DropToPoolAt(path, _pool, index);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Figure out where in the tile list a drop lands: before/after whichever tile
    /// the cursor is over (by its horizontal half), or at the end if over empty space.
    /// </summary>
    private int ComputeDropIndex(Point p)
    {
        for (int i = 0; i < Tiles.Children.Count; i++)
        {
            if (Tiles.Children[i] is FrameworkElement fe)
            {
                var tl = fe.TranslatePoint(new Point(0, 0), Tiles);
                var rect = new Rect(tl.X, tl.Y, fe.ActualWidth, fe.ActualHeight);
                if (rect.Contains(p))
                    return p.X < rect.Left + rect.Width / 2 ? i : i + 1;
            }
        }
        return Tiles.Children.Count;
    }

    // ---- Resize -----------------------------------------------------------

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var edges = (sender as FrameworkElement)?.Tag as string ?? "";
        ApplyResize(edges, e.HorizontalChange, e.VerticalChange, finalize: false);
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var edges = (sender as FrameworkElement)?.Tag as string ?? "";
        ApplyResize(edges, 0, 0, finalize: true); // snap once, on release
        _manager.Save();
    }

    /// <summary>
    /// Resize from any edge/corner. During a drag (finalize=false) movement is
    /// free so the edge is easy to grab and move; on release (finalize=true) the
    /// active snap mode is applied, anchored to the opposite edge so the edge you
    /// dragged is the one that snaps.
    /// </summary>
    private void ApplyResize(string edges, double dx, double dy, bool finalize)
    {
        bool left = edges.Contains('L'), right = edges.Contains('R');
        bool top = edges.Contains('T'), bottom = edges.Contains('B');

        double newLeft = Left, newTop = Top, w = _pool.Width, h = _pool.Height;
        if (right) w += dx;
        if (bottom) h += dy;
        if (left) { w -= dx; newLeft += dx; }
        if (top) { h -= dy; newTop += dy; }

        if (finalize && (_pool.AutoSnap || _pool.MagneticSnap))
        {
            double sw, sh;
            if (_pool.AutoSnap)
                (sw, sh) = GridSizer.Snap(w, Tiles.Children.Count, CellW, CellH, BodyPad, TitleHeight);
            else
            {
                sw = GridSizer.MagneticSnap(w, CellW, BodyPad, SnapRange);
                sh = GridSizer.MagneticSnap(h, CellH, TitleHeight + BodyPad, SnapRange);
            }
            if (left) newLeft += w - sw; // keep the right edge fixed
            if (top) newTop += h - sh;   // keep the bottom edge fixed
            w = sw; h = sh;
        }

        // Clamp to minimum, keeping the anchored edge fixed for left/top drags.
        if (w < MinWidth_) { if (left) newLeft -= MinWidth_ - w; w = MinWidth_; }
        if (h < MinHeight_) { if (top) newTop -= MinHeight_ - h; h = MinHeight_; }

        Left = newLeft;
        Top = newTop;
        _pool.Left = newLeft;
        _pool.Top = newTop;
        _pool.Width = w;
        _pool.Height = h;
        Width = w;
        Height = _pool.RolledUp ? TitleHeight : h;
        UpdateScreenRect();
    }

    /// <summary>
    /// Size the pool to a whole grid of cells with even padding: columns come from
    /// the proposed width, rows are however many are needed to show every tile.
    /// </summary>
    private void ApplyAutoSnap(double proposedWidth)
    {
        var (w, h) = GridSizer.Snap(proposedWidth, Tiles.Children.Count, CellW, CellH, BodyPad, TitleHeight);
        _pool.Width = w;
        _pool.Height = h;
        Width = w;
        if (!_pool.RolledUp) Height = h;
    }

    private void AutoSnap_Click(object sender, RoutedEventArgs e)
    {
        _pool.AutoSnap = !_pool.AutoSnap;
        if (_pool.AutoSnap) _pool.MagneticSnap = false; // the two modes are exclusive
        AutoSnapMenuItem.IsChecked = _pool.AutoSnap;
        MagneticSnapMenuItem.IsChecked = _pool.MagneticSnap;
        if (_pool.AutoSnap) ApplyAutoSnap(_pool.Width); // tidy up immediately
        UpdateScreenRect();
        _manager.Save();
    }

    private void MagneticSnap_Click(object sender, RoutedEventArgs e)
    {
        _pool.MagneticSnap = !_pool.MagneticSnap;
        if (_pool.MagneticSnap) _pool.AutoSnap = false; // the two modes are exclusive
        MagneticSnapMenuItem.IsChecked = _pool.MagneticSnap;
        AutoSnapMenuItem.IsChecked = _pool.AutoSnap;
        _manager.Save();
    }

    // ---- Context menu -----------------------------------------------------

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (ShowAppearanceDialog(_pool.Color, _pool.Opacity, out string color, out double opacity))
        {
            _pool.Color = color;
            _pool.Opacity = opacity;
            ApplyAppearance();
            _manager.Save();
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptForText("Rename pool", _pool.Title);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _pool.Title = name.Trim();
            TitleText.Text = _pool.Title;
            _manager.Save();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete the pool \"{_pool.Title}\"? Its items return to the desktop.",
            "Shimmer", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        _coord.DeletePool(_pool); // App removes the pool and closes this window
    }

    // ---- Small dialogs ----------------------------------------------------

    private static string? PromptForText(string title, string initial)
    {
        var dialog = new Window
        {
            Title = title, Width = 320, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
        };
        var box = new TextBox { Text = initial, Margin = new Thickness(12, 12, 12, 6) };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        var panel = new DockPanel();
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(box);
        dialog.Content = panel;

        bool okPressed = false;
        ok.Click += (_, _) => { okPressed = true; dialog.Close(); };
        box.Focus();
        box.SelectAll();
        dialog.ShowDialog();
        return okPressed ? box.Text : null;
    }

    private static bool ShowAppearanceDialog(string initialColor, double initialOpacity,
        out string color, out double opacity)
    {
        color = initialColor;
        opacity = initialOpacity;

        var dialog = new Window
        {
            Title = "Pool appearance", Width = 300,
            SizeToContent = SizeToContent.Height, // auto-fit so the OK button is never clipped
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
        };
        var root = new StackPanel { Margin = new Thickness(14) };

        Color startColor;
        try { startColor = (Color)ColorConverter.ConvertFromString(initialColor); }
        catch { startColor = Color.FromRgb(0x3A, 0x6E, 0xA5); }

        root.Children.Add(new TextBlock { Text = "Color", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var picker = new ColorPickerControl(startColor);
        root.Children.Add(picker);

        // Live preview + editable hex, kept in sync with the picker.
        var hexBox = new TextBox { Width = 100, VerticalAlignment = VerticalAlignment.Center };
        var preview = new Border
        {
            Width = 40, Height = 24, Margin = new Thickness(8, 0, 0, 0),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(startColor),
        };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(preview);
        root.Children.Add(hexRow);

        bool syncing = false;
        static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        hexBox.Text = ToHex(startColor);

        picker.ColorChanged += () =>
        {
            if (syncing) return;
            syncing = true;
            hexBox.Text = ToHex(picker.SelectedColor);
            preview.Background = new SolidColorBrush(picker.SelectedColor);
            syncing = false;
        };
        hexBox.TextChanged += (_, _) =>
        {
            if (syncing) return;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hexBox.Text);
                syncing = true;
                picker.SetColor(c);
                preview.Background = new SolidColorBrush(c);
                syncing = false;
            }
            catch { /* mid-typing / invalid hex: ignore */ }
        };

        root.Children.Add(new TextBlock { Text = "Opacity", Margin = new Thickness(0, 12, 0, 2), FontWeight = FontWeights.SemiBold });
        var slider = new Slider { Minimum = 3, Maximum = 100, Value = Math.Clamp(initialOpacity * 100, 3, 100) };
        var pct = new TextBlock { Text = $"{slider.Value:0}%" };
        slider.ValueChanged += (_, _) => pct.Text = $"{slider.Value:0}%";
        root.Children.Add(slider);
        root.Children.Add(pct);

        var ok = new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0), IsDefault = true };
        root.Children.Add(ok);
        dialog.Content = root;

        bool okPressed = false;
        ok.Click += (_, _) => { okPressed = true; dialog.Close(); };
        dialog.ShowDialog();

        if (!okPressed) return false;
        color = ToHex(picker.SelectedColor);
        opacity = slider.Value / 100.0;
        return true;
    }
}
