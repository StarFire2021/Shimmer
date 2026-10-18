using System.Runtime.InteropServices;

namespace Shimmer.Interop;

/// <summary>
/// Raw P/Invoke surface. Everything the app needs to talk to the Windows shell
/// lives here so the rest of the codebase stays free of <c>[DllImport]</c> noise.
///
/// The interesting/dangerous calls are the cross-process ones (OpenProcess,
/// VirtualAllocEx, WriteProcessMemory, ReadProcessMemory). The desktop's icon
/// grid is a <c>SysListView32</c> owned by explorer.exe. ListView messages like
/// LVM_GETITEMPOSITION expect a *pointer* in lParam, and that pointer is
/// dereferenced inside explorer's address space — not ours. So to read or move a
/// desktop icon we have to allocate a scratch buffer inside explorer, poke our
/// request into it, SendMessage, then read explorer's reply back out. That is the
/// whole trick behind Shimmer's desktop-icon interop.
/// </summary>
internal static class NativeMethods
{
    // ---- Window discovery -------------------------------------------------

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string? lpszClass, string? lpszWindow);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    // ---- Shell: icon + display name extraction ---------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint SHGFI_DISPLAYNAME = 0x000000200;

    // ShowWindow commands
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;

    // Translate between a window's client space and screen space. The desktop
    // ListView's client origin is the top-left of the *virtual* screen, which is
    // not (0,0) on multi-monitor setups, so these are essential for mapping a
    // pool's screen rectangle onto icon coordinates.
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    // ---- Messaging --------------------------------------------------------
    // Two overloads: one that passes an int in lParam (counts, packed coords),
    // one that passes a pointer (into the target process) for struct exchange.

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, int lParam);

    // ---- Cross-process memory --------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // ---- Window positioning (used to pin pool windows to the desktop) ----

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- Structs ----------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// LVITEMW. Field order and sizes must match the native 64-bit struct exactly
    /// because we blit it straight into explorer.exe's memory. IntPtr is 8 bytes
    /// on x64 (which is why the .csproj forces PlatformTarget=x64).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;   // pointer INTO the target process
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    // ---- Constants --------------------------------------------------------

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080; // keep off Alt-Tab
    internal const int WS_EX_NOACTIVATE = 0x08000000; // don't steal focus
    internal const int WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through
    internal const int WS_EX_LAYERED = 0x00080000;     // required for per-window opacity

    // SetWindowPos flags / z-order targets
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    // Continuously re-pinning z-order via WM_WINDOWPOSCHANGING keeps our desktop
    // windows from floating above real app windows.
    internal const int WM_WINDOWPOSCHANGING = 0x0046;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    // ListView messages (LVM_FIRST = 0x1000)
    private const uint LVM_FIRST = 0x1000;
    internal const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    internal const uint LVM_GETITEMRECT = LVM_FIRST + 14;
    internal const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
    internal const uint LVM_GETORIGIN = LVM_FIRST + 41; // scroll origin (POINT in lParam)
    internal const uint LVM_SETITEMPOSITION32 = LVM_FIRST + 49; // POINT in lParam
    internal const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;
    internal const uint LVM_REDRAWITEMS = LVM_FIRST + 21;

    // LVITEM mask flags
    internal const uint LVIF_TEXT = 0x0001;

    // OpenProcess access rights
    internal const uint PROCESS_VM_OPERATION = 0x0008;
    internal const uint PROCESS_VM_READ = 0x0010;
    internal const uint PROCESS_VM_WRITE = 0x0020;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;

    // VirtualAllocEx
    internal const uint MEM_COMMIT = 0x1000;
    internal const uint MEM_RESERVE = 0x2000;
    internal const uint MEM_RELEASE = 0x8000;
    internal const uint PAGE_READWRITE = 0x04;
}
