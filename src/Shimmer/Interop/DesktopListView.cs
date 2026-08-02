using System.Runtime.InteropServices;
using System.Text;
using Shimmer.Models;
using static Shimmer.Interop.NativeMethods;

namespace Shimmer.Interop;

/// <summary>
/// A live session against the desktop's icon ListView (explorer.exe). Construct
/// one, use it, dispose it. It owns a handle to explorer and a small scratch
/// buffer allocated *inside* explorer's address space that every read/write
/// reuses.
///
/// Coordinate space: all X/Y values are pixels relative to the ListView client
/// area, which for the desktop is effectively the virtual screen origin. Reads
/// (GetItemPosition) and writes (SetItemPosition) share that space, so a value
/// you read back can be handed straight to a move.
/// </summary>
public sealed class DesktopListView : IDisposable
{
    private const int ScratchSize = 4096;
    private const int TextOffset = 512;   // where item-text lands inside the scratch buffer
    private const int MaxTextChars = 512; // 1024 bytes, comfortably inside ScratchSize

    private readonly IntPtr _listView;
    private readonly IntPtr _process;
    private readonly IntPtr _scratch;     // address inside explorer.exe
    private readonly (int X, int Y) _origin; // ListView scroll origin, cached
    private bool _disposed;

    public bool IsAvailable => _listView != IntPtr.Zero && _process != IntPtr.Zero && _scratch != IntPtr.Zero;

    public DesktopListView()
    {
        _listView = FindDesktopListView();
        if (_listView == IntPtr.Zero)
            throw new InvalidOperationException("Could not locate the desktop ListView (SysListView32).");

        GetWindowThreadProcessId(_listView, out uint pid);
        _process = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (_process == IntPtr.Zero)
            throw new InvalidOperationException($"OpenProcess on explorer.exe (pid {pid}) failed: {Marshal.GetLastWin32Error()}.");

        _scratch = VirtualAllocEx(_process, IntPtr.Zero, ScratchSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (_scratch == IntPtr.Zero)
            throw new InvalidOperationException($"VirtualAllocEx in explorer.exe failed: {Marshal.GetLastWin32Error()}.");

        _origin = GetOrigin();
    }

    // ---- Coordinate conversion -------------------------------------------
    // Icon positions from LVM_GETITEMPOSITION are in the ListView's *view*
    // coordinates: client coordinates shifted by the scroll origin. The desktop
    // ListView's client origin is the top-left of the virtual screen (which can
    // be negative with monitors arranged above/left of the primary). These two
    // helpers convert between an icon's view coordinates and true screen pixels,
    // so a fence's on-screen rectangle can be compared against — and used to
    // place — real icons.

    /// <summary>View (icon) coordinates -> screen pixels.</summary>
    public (int X, int Y) ItemToScreen(int viewX, int viewY)
    {
        var p = new POINT { X = viewX - _origin.X, Y = viewY - _origin.Y }; // view -> client
        ClientToScreen(_listView, ref p);                                    // client -> screen
        return (p.X, p.Y);
    }

    /// <summary>Screen pixels -> view (icon) coordinates.</summary>
    public (int X, int Y) ScreenToItem(int screenX, int screenY)
    {
        var p = new POINT { X = screenX, Y = screenY };
        ScreenToClient(_listView, ref p);                    // screen -> client
        return (p.X + _origin.X, p.Y + _origin.Y);           // client -> view
    }

    private (int X, int Y) GetOrigin()
    {
        // Clear the buffer, then let the ListView write its origin POINT into it.
        WriteProcessMemory(_process, _scratch, new byte[8], 8, out _);
        SendMessage(_listView, LVM_GETORIGIN, IntPtr.Zero, _scratch);
        byte[] buf = new byte[8];
        ReadProcessMemory(_process, _scratch, buf, buf.Length, out _);
        return (BitConverter.ToInt32(buf, 0), BitConverter.ToInt32(buf, 4));
    }

    /// <summary>Number of icons currently on the desktop.</summary>
    public int Count => (int)SendMessage(_listView, LVM_GETITEMCOUNT, IntPtr.Zero, 0);

    /// <summary>Snapshot every icon (index, label, position) in one pass.</summary>
    public IReadOnlyList<DesktopIcon> GetIcons()
    {
        int count = Count;
        var list = new List<DesktopIcon>(count);
        for (int i = 0; i < count; i++)
        {
            var (x, y) = GetItemPosition(i);
            string label = GetItemText(i);
            list.Add(new DesktopIcon(i, label, x, y));
        }
        return list;
    }

    /// <summary>Read one icon's position (client pixels).</summary>
    public (int X, int Y) GetItemPosition(int index)
    {
        // LVM_GETITEMPOSITION writes a POINT into the buffer we point it at.
        SendMessage(_listView, LVM_GETITEMPOSITION, (IntPtr)index, _scratch);
        byte[] buf = new byte[8];
        ReadProcessMemory(_process, _scratch, buf, buf.Length, out _);
        int x = BitConverter.ToInt32(buf, 0);
        int y = BitConverter.ToInt32(buf, 4);
        return (x, y);
    }

    /// <summary>Move one icon. This is how we hide (park off-screen) and reveal.</summary>
    public void SetItemPosition(int index, int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        byte[] buf = new byte[8];
        BitConverter.GetBytes(pt.X).CopyTo(buf, 0);
        BitConverter.GetBytes(pt.Y).CopyTo(buf, 4);
        WriteProcessMemory(_process, _scratch, buf, buf.Length, out _);
        SendMessage(_listView, LVM_SETITEMPOSITION32, (IntPtr)index, _scratch);
    }

    /// <summary>Read one icon's caption via a remote LVITEM whose text pointer lives in explorer.</summary>
    public string GetItemText(int index)
    {
        var lvi = new LVITEM
        {
            mask = LVIF_TEXT,
            iItem = index,
            iSubItem = 0,
            pszText = _scratch + TextOffset,   // buffer inside explorer for the reply
            cchTextMax = MaxTextChars,
        };

        int size = Marshal.SizeOf<LVITEM>();
        IntPtr local = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(lvi, local, false);
            byte[] lviBytes = new byte[size];
            Marshal.Copy(local, lviBytes, 0, size);
            WriteProcessMemory(_process, _scratch, lviBytes, size, out _);

            int chars = (int)SendMessage(_listView, LVM_GETITEMTEXTW, (IntPtr)index, _scratch);
            if (chars <= 0) return string.Empty;

            int bytes = Math.Min(chars, MaxTextChars) * 2;
            byte[] textBuf = new byte[bytes];
            ReadProcessMemory(_process, _scratch + TextOffset, textBuf, bytes, out _);
            return Encoding.Unicode.GetString(textBuf);
        }
        finally
        {
            Marshal.FreeHGlobal(local);
        }
    }

    /// <summary>Ask the desktop to repaint after we have shuffled icons around.</summary>
    public void Refresh()
    {
        int count = Count;
        if (count > 0)
            SendMessage(_listView, LVM_REDRAWITEMS, IntPtr.Zero, count - 1);
    }

    // ---- Locating the desktop ListView -----------------------------------
    // Normal case: Progman > SHELLDLL_DefView > SysListView32.
    // With certain wallpaper/slideshow states, explorer parks SHELLDLL_DefView
    // under a WorkerW window instead, so we fall back to scanning top-level
    // windows for that pattern.
    /// <summary>Public handle lookup so other components (e.g. hide/show) can reuse it.</summary>
    public static IntPtr FindListViewHandle() => FindDesktopListView();

    private static IntPtr FindDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (shellView == IntPtr.Zero)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                IntPtr sv = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (sv != IntPtr.Zero)
                {
                    found = sv;
                    return false; // stop enumerating
                }
                return true;
            }, IntPtr.Zero);
            shellView = found;
        }

        if (shellView == IntPtr.Zero) return IntPtr.Zero;
        return FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_scratch != IntPtr.Zero && _process != IntPtr.Zero)
            VirtualFreeEx(_process, _scratch, 0, MEM_RELEASE);
        if (_process != IntPtr.Zero)
            CloseHandle(_process);
    }
}
