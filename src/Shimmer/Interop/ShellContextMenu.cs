using System.Runtime.InteropServices;

namespace Shimmer.Interop;

/// <summary>
/// Shows the real Windows shell context menu (Open, Cut, Copy, Create shortcut,
/// Delete, Rename, Properties, plus any app-installed verbs) for a file path.
///
/// The shell exposes this via COM: get the item's PIDL, bind to its parent
/// folder, ask that folder for the item's <c>IContextMenu</c>, populate a native
/// popup menu, track it, and invoke whatever the user picked. It's fiddly, but it
/// is the only way to get the genuine menu rather than a hand-rolled imitation.
/// </summary>
public static class ShellContextMenu
{
    public static void Show(string path, IntPtr ownerHwnd, int screenX, int screenY)
    {
        IntPtr pidl = ILCreateFromPath(path);
        if (pidl == IntPtr.Zero) return;

        Guid iidShellFolder = IID_IShellFolder;
        int hr = SHBindToParent(pidl, ref iidShellFolder, out IntPtr psfPtr, out IntPtr childPidl);
        if (hr != 0 || psfPtr == IntPtr.Zero)
        {
            ILFree(pidl);
            return;
        }

        var parent = (IShellFolder)Marshal.GetObjectForIUnknown(psfPtr);
        IntPtr pcmPtr = IntPtr.Zero;
        try
        {
            Guid iidContextMenu = IID_IContextMenu;
            IntPtr[] apidl = { childPidl };
            hr = parent.GetUIObjectOf(ownerHwnd, 1, apidl, ref iidContextMenu, IntPtr.Zero, out pcmPtr);
            if (hr != 0 || pcmPtr == IntPtr.Zero) return;

            var menu = (IContextMenu)Marshal.GetObjectForIUnknown(pcmPtr);
            IntPtr hMenu = CreatePopupMenu();
            try
            {
                const uint idFirst = 1, idLast = 0x7FFF;
                menu.QueryContextMenu(hMenu, 0, idFirst, idLast, CMF_NORMAL);

                // TrackPopupMenu needs a foreground owner to place and dismiss the
                // menu correctly; the trailing WM_NULL is the standard trick to
                // make it dismiss cleanly when clicking elsewhere.
                SetForegroundWindow(ownerHwnd);
                uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                    screenX, screenY, ownerHwnd, IntPtr.Zero);
                PostMessage(ownerHwnd, 0x0000 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);

                if (cmd > 0)
                {
                    var info = new CMINVOKECOMMANDINFO
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        hwnd = ownerHwnd,
                        lpVerb = (IntPtr)(cmd - idFirst), // MAKEINTRESOURCE(offset)
                        nShow = 1, // SW_SHOWNORMAL
                    };
                    menu.InvokeCommand(ref info);
                }
            }
            finally
            {
                DestroyMenu(hMenu);
                Marshal.ReleaseComObject(menu);
            }
        }
        finally
        {
            if (pcmPtr != IntPtr.Zero) Marshal.Release(pcmPtr);
            Marshal.ReleaseComObject(parent);
            Marshal.Release(psfPtr);
            ILFree(pidl); // childPidl points inside pidl; do not free separately
        }
    }

    // ---- COM interfaces ---------------------------------------------------

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, [MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }

    private const uint CMF_NORMAL = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    // ---- P/Invoke ---------------------------------------------------------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPath(string pszPath);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
