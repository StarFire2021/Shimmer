using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Shimmer.Interop;
using Shimmer.Models;
using Shimmer.Services;
using Shimmer.Windows;
using WinFormsCursor = System.Windows.Forms.Cursor;

namespace Shimmer.Controls;

/// <summary>
/// One desktop item rendered as a tile (icon + label), used identically on the
/// loose desktop layer and inside fences. Behaviours:
///   * double-click  -> launch via the shell
///   * right-click   -> the real Windows shell context menu
///   * left-drag     -> OLE drag so it can be dropped into a fence or back onto
///                      the desktop (works across our separate windows)
/// </summary>
public sealed class DesktopTile : Border
{
    public DesktopItem Item { get; }
    private readonly IDesktopCoordinator _coord;

    private Point _dragStart;
    private bool _armed;
    private DragGhost? _ghost;

    public DesktopTile(DesktopItem item, IDesktopCoordinator coord)
    {
        Item = item;
        _coord = coord;

        Width = 84;
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)); // subtle + hittable
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(6, 8, 6, 6);
        Margin = new Thickness(4);
        Cursor = Cursors.Hand;
        ToolTip = item.Name;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new Image
        {
            Source = item.Icon,
            Width = 40,
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Name,
            Foreground = Brushes.White,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 30,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Child = stack;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            Launch();
            _armed = false;
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _armed = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_armed || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _armed = false;
        _coord.BeginDrag();
        _ghost = new DragGhost(Item.Icon);
        _ghost.Show();
        try
        {
            var data = new DataObject(DragFormats.Item, Item.Path);
            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }
        finally
        {
            _ghost.Close();
            _ghost = null;
            _coord.EndDrag();
        }
    }

    protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);
        if (_ghost is not null)
        {
            var p = WinFormsCursor.Position; // physical pixels
            _ghost.MoveTo(p.X, p.Y);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _armed = false;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var host = Window.GetWindow(this);
        if (host is null) return;
        var hwnd = new WindowInteropHelper(host).Handle;
        var screen = PointToScreen(e.GetPosition(this));
        ShellContextMenu.Show(Item.Path, hwnd, (int)screen.X, (int)screen.Y);
        e.Handled = true;
    }

    private void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Item.Path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open \"{Item.Name}\":\n{ex.Message}", "Shimmer");
        }
    }
}
