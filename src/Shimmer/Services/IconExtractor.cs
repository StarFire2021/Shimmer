using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Services;

/// <summary>
/// Pulls a file's real Windows icon and its shell display name via SHGetFileInfo.
/// The icon comes back as a native HICON which we convert into a frozen WPF
/// <see cref="ImageSource"/> (frozen so it can cross threads and is cheap to
/// render), then destroy the HICON to avoid leaking GDI handles.
/// </summary>
public static class IconExtractor
{
    public static ImageSource? GetIcon(string path)
    {
        var shfi = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (shfi.hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    /// <summary>The name the desktop would show (shortcuts without the .lnk, etc.).</summary>
    public static string GetDisplayName(string path)
    {
        var shfi = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_DISPLAYNAME);
        return string.IsNullOrEmpty(shfi.szDisplayName)
            ? System.IO.Path.GetFileName(path)
            : shfi.szDisplayName;
    }
}
