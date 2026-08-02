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
/// A fence: a tinted container window that renders its member items as tiles.
/// Supports drag-in / drag-out (drops), resize, color/opacity, and whole-fence
/// hide-until-hover (the entire window vanishes until the cursor enters its area).
/// </summary>
public partial class FenceWindow : Window
{
    private const double TitleHeight = 34;
    private const double MinWidth_ = 180;
    private const double MinHeight_ = 130;

    private readonly Fence _fence;
    private readonly FenceManager _manager;
    private readonly IDesktopCoordinator _coord;
    private readonly IntPtr _looseHwnd;
    private readonly DispatcherTimer _hoverTimer;
    private Rect _screenRectPx;

    public Fence Fence => _fence;

    public FenceWindow(Fence fence, FenceManager manager, IDesktopCoordinator coord, IntPtr looseHwnd)
    {
        InitializeComponent();
        _fence = fence;
        _manager = manager;
        _coord = coord;
        _looseHwnd = looseHwnd;

        Left = fence.Left;
        Top = fence.Top;
        Width = fence.Width;
        Height = fence.Height;
        TitleText.Text = fence.Title;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _hoverTimer.Tick += HoverTimer_Tick;

        Loaded += FenceWindow_Loaded;
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

    // Keep the fence pinned directly above the loose layer (and thus below all
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

    private void FenceWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        Rebuild();
        AutoHideMenuItem.IsChecked = _fence.AutoHide;
        ApplyRolled();
        UpdateScreenRect();
        if (_fence.AutoHide)
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
        foreach (var item in _coord.MembersOf(_fence))
            Tiles.Children.Add(new DesktopTile(item, _coord));
    }

    private void ApplyAppearance()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_fence.Color);
            byte a = (byte)(Math.Clamp(_fence.Opacity, 0, 1) * 255);
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
        _fence.RolledUp = !_fence.RolledUp;
        ApplyRolled();
        _manager.Save();
    }

    /// <summary>Collapse to just the title bar (rolled up) or restore the body.</summary>
    private void ApplyRolled()
    {
        BodyHost.Visibility = _fence.RolledUp ? Visibility.Collapsed : Visibility.Visible;
        Height = _fence.RolledUp ? TitleHeight : _fence.Height;
        RollMenuItem.IsChecked = _fence.RolledUp;
        RollButton.Content = _fence.RolledUp ? "▸" : "▾";
        UpdateScreenRect();
    }

    // ---- Hide until hover -------------------------------------------------

    private void AutoHide_Click(object sender, RoutedEventArgs e)
    {
        _fence.AutoHide = !_fence.AutoHide;
        AutoHideMenuItem.IsChecked = _fence.AutoHide;
        _manager.Save();

        if (_fence.AutoHide)
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
        if (!_fence.AutoHide) { _hoverTimer.Stop(); return; }

        if (CursorInside())
        {
            if (Visibility != Visibility.Visible)
            {
                // Visibility.Hidden preserves z-order, so no re-sink needed — the
                // fence returns exactly where it was (above the loose layer).
                Visibility = Visibility.Visible;
                UpdateScreenRect();
            }
        }
        else if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Hidden;
        }
    }

    // ---- Drag to move -----------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        _fence.Left = Left;
        _fence.Top = Top;
        UpdateScreenRect();
        _manager.Save();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _fence.Left = Left;
        _fence.Top = Top;
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
            _coord.DropToFence(path, _fence);
            e.Handled = true;
        }
    }

    // ---- Resize -----------------------------------------------------------

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        => SetFenceSize(_fence.Width + e.HorizontalChange, _fence.Height);

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
        => SetFenceSize(_fence.Width, _fence.Height + e.VerticalChange);

    private void ResizeCorner_DragDelta(object sender, DragDeltaEventArgs e)
        => SetFenceSize(_fence.Width + e.HorizontalChange, _fence.Height + e.VerticalChange);

    private void Resize_Completed(object sender, DragCompletedEventArgs e)
    {
        UpdateScreenRect();
        _manager.Save();
    }

    private void SetFenceSize(double width, double height)
    {
        _fence.Width = Math.Max(MinWidth_, width);
        _fence.Height = Math.Max(MinHeight_, height);
        Width = _fence.Width;
        Height = _fence.Height;
    }

    // ---- Context menu -----------------------------------------------------

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (ShowAppearanceDialog(_fence.Color, _fence.Opacity, out string color, out double opacity))
        {
            _fence.Color = color;
            _fence.Opacity = opacity;
            ApplyAppearance();
            _manager.Save();
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptForText("Rename fence", _fence.Title);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _fence.Title = name.Trim();
            TitleText.Text = _fence.Title;
            _manager.Save();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete the fence \"{_fence.Title}\"? Its items return to the desktop.",
            "Shimmer", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        _coord.DeleteFence(_fence); // App removes the fence and closes this window
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
            Title = "Fence appearance", Width = 300,
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
