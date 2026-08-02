using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shimmer.Controls;

/// <summary>
/// A compact visual colour picker: a 2-D saturation/brightness field you point
/// into, plus a vertical hue (rainbow) strip. Exposes the chosen RGB colour and
/// raises <see cref="ColorChanged"/> as the user drags. Built in code (WPF has no
/// stock picker) so it can drop straight into the appearance dialog.
/// </summary>
public sealed class ColorPickerControl : Grid
{
    private const double SvW = 200, SvH = 150, HueW = 22;

    private readonly Canvas _sv;
    private readonly Rectangle _svHue;      // white -> pure-hue (horizontal)
    private readonly Ellipse _svCursor;
    private readonly Canvas _hue;
    private readonly Rectangle _hueMarker;

    private double _h, _s, _v; // HSV state (h in 0..360, s/v in 0..1)

    public event Action? ColorChanged;

    public Color SelectedColor => HsvToRgb(_h, _s, _v);

    public ColorPickerControl(Color initial)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // --- Saturation / brightness field ---
        _sv = new Canvas { Width = SvW, Height = SvH, ClipToBounds = true, Cursor = Cursors.Cross };
        _svHue = new Rectangle { Width = SvW, Height = SvH };
        var black = new Rectangle
        {
            Width = SvW,
            Height = SvH,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)),
            IsHitTestVisible = false,
        };
        _svCursor = new Ellipse
        {
            Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        _sv.Children.Add(_svHue);
        _sv.Children.Add(black);
        _sv.Children.Add(_svCursor);
        _sv.MouseLeftButtonDown += (s, e) => { _sv.CaptureMouse(); PickSv(e.GetPosition(_sv)); };
        _sv.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) PickSv(e.GetPosition(_sv)); };
        _sv.MouseLeftButtonUp += (s, e) => _sv.ReleaseMouseCapture();
        SetColumn(_sv, 0);
        Children.Add(_sv);

        // --- Hue strip ---
        _hue = new Canvas { Width = HueW, Height = SvH, Cursor = Cursors.SizeNS };
        var hueRect = new Rectangle { Width = HueW, Height = SvH, Fill = BuildHueBrush() };
        _hueMarker = new Rectangle
        {
            Width = HueW, Height = 3, Stroke = Brushes.White, StrokeThickness = 1,
            Fill = Brushes.Transparent, IsHitTestVisible = false,
        };
        _hue.Children.Add(hueRect);
        _hue.Children.Add(_hueMarker);
        _hue.MouseLeftButtonDown += (s, e) => { _hue.CaptureMouse(); PickHue(e.GetPosition(_hue)); };
        _hue.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) PickHue(e.GetPosition(_hue)); };
        _hue.MouseLeftButtonUp += (s, e) => _hue.ReleaseMouseCapture();
        SetColumn(_hue, 2);
        Children.Add(_hue);

        SetColor(initial);
    }

    /// <summary>Set the picker to a specific colour (e.g. from the hex box).</summary>
    public void SetColor(Color c)
    {
        (_h, _s, _v) = RgbToHsv(c);
        UpdateVisuals();
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / SvW, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvH, 0, 1);
        UpdateVisuals();
    }

    private void PickHue(Point p)
    {
        _h = Math.Clamp(p.Y / SvH, 0, 1) * 360;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        // Base of the SV field is white -> the pure current hue.
        _svHue.Fill = new LinearGradientBrush(Colors.White, HsvToRgb(_h, 1, 1), new Point(0, 0), new Point(1, 0));

        Canvas.SetLeft(_svCursor, _s * SvW - _svCursor.Width / 2);
        Canvas.SetTop(_svCursor, (1 - _v) * SvH - _svCursor.Height / 2);
        Canvas.SetTop(_hueMarker, _h / 360 * SvH - _hueMarker.Height / 2);

        ColorChanged?.Invoke();
    }

    private static LinearGradientBrush BuildHueBrush()
    {
        var stops = new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(255, 0, 0), 0.0),
            new GradientStop(Color.FromRgb(255, 255, 0), 1.0 / 6),
            new GradientStop(Color.FromRgb(0, 255, 0), 2.0 / 6),
            new GradientStop(Color.FromRgb(0, 255, 255), 3.0 / 6),
            new GradientStop(Color.FromRgb(0, 0, 255), 4.0 / 6),
            new GradientStop(Color.FromRgb(255, 0, 255), 5.0 / 6),
            new GradientStop(Color.FromRgb(255, 0, 0), 1.0),
        };
        return new LinearGradientBrush(stops) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
    }

    // ---- HSV <-> RGB ------------------------------------------------------

    private static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;

        double h = 0;
        if (d != 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
        }
        if (h < 0) h += 360;
        double s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
