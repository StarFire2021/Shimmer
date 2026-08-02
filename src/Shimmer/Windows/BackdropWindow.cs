using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Windows;

/// <summary>
/// The tinted panel that sits *behind* a fence's icons. It is a second, separate
/// top-level window per fence, made click-through with WS_EX_TRANSPARENT so mouse
/// input falls straight past its colored fill to the real desktop icons beneath —
/// that is what lets us show a solid backdrop without covering (or blocking) the
/// icons. WS_EX_LAYERED lets us drive the whole panel's translucency with
/// Window.Opacity.
///
/// The owning <see cref="FenceWindow"/> keeps this in lockstep: same position and
/// size, shown when the fence is open, hidden when it is rolled up.
/// </summary>
public sealed class BackdropWindow : Window
{
    private readonly Border _fill;

    public BackdropWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        IsHitTestVisible = false; // belt-and-braces; WS_EX_TRANSPARENT does the real work

        _fill = new Border { CornerRadius = new CornerRadius(10) };
        Content = _fill;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SinkToBottom();
    }

    /// <summary>Match the fence's geometry (device-independent units).</summary>
    public void UpdateGeometry(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    /// <summary>Apply the fence's chosen tint color and opacity.</summary>
    public void SetAppearance(string colorHex, double opacity)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            _fill.Background = new SolidColorBrush(color);
        }
        catch
        {
            _fill.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5));
        }
        Opacity = Math.Clamp(opacity, 0.03, 1.0);
    }

    /// <summary>Keep the panel pinned at the very bottom, beneath the fence chrome.</summary>
    public void SinkToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
