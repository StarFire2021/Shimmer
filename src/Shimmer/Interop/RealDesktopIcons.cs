using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Interop;

/// <summary>
/// Hides and restores the *real* Windows desktop icon layer. This is the key to
/// the whole approach: rather than shoving Explorer's icons around (which it
/// keeps snapping back), we simply stop Explorer from drawing them and render our
/// own tiles instead.
///
/// The hide is a pure display change — we call ShowWindow on the desktop's icon
/// ListView (SysListView32). No files move, no attributes change, and Explorer
/// keeps every icon and its saved position. The moment we call <see cref="Show"/>
/// (on exit, on crash-recovery, or from the tray) the normal desktop is back.
///
/// We hide only the ListView, not its parent SHELLDLL_DefView, so the desktop
/// right-click menu on the empty background still works.
/// </summary>
public static class RealDesktopIcons
{
    public static IntPtr Handle => DesktopListView.FindListViewHandle();

    public static bool AreHidden()
    {
        var h = Handle;
        return h != IntPtr.Zero && !IsWindowVisible(h);
    }

    public static void Hide()
    {
        var h = Handle;
        if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE);
    }

    public static void Show()
    {
        var h = Handle;
        if (h != IntPtr.Zero) ShowWindow(h, SW_SHOW);
    }

    /// <summary>Re-hide if Explorer has re-shown the icons (e.g. after a refresh).</summary>
    public static void EnsureHidden()
    {
        var h = Handle;
        if (h != IntPtr.Zero && IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }
}
