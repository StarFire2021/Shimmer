using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Shimmer.Interop;
using Shimmer.Models;
using Shimmer.Services;
using Shimmer.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Shimmer;

/// <summary>
/// Tray host and desktop coordinator. Hides Explorer's real icons and renders our
/// own: a loose layer for un-fenced items and a window per fence. Implements
/// <see cref="IDesktopCoordinator"/> so tiles, the loose layer, and fences can
/// resolve membership and apply drops through one brain.
/// </summary>
public partial class App : System.Windows.Application, IDesktopCoordinator
{
    private Forms.NotifyIcon _tray = null!;
    private LooseStore _looseStore = null!;
    private FenceManager _manager = null!;
    private LooseLayerWindow? _looseLayer;
    private readonly List<FenceWindow> _fenceWindows = new();
    private Dictionary<string, DesktopItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer _hideWatch = null!;
    private bool _iconsHidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, _) =>
        {
            try { RealDesktopIcons.Show(); } catch { }
        };

        _looseStore = new LooseStore();
        _manager = new FenceManager(new FenceStore());
        SetupTray();

        HideRealIconsAndRender();

        _hideWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _hideWatch.Tick += (_, _) => { if (_iconsHidden) RealDesktopIcons.EnsureHidden(); };
        _hideWatch.Start();
    }

    // ---- Render / rescan --------------------------------------------------

    private void HideRealIconsAndRender()
    {
        RealDesktopIcons.Hide();
        _iconsHidden = true;

        var items = DesktopScanner.Scan();
        _items = items.ToDictionary(i => i.Path, i => i, StringComparer.OrdinalIgnoreCase);

        CloseAllWindows();

        _looseLayer = new LooseLayerWindow(this, _looseStore);
        _looseLayer.Show();

        IntPtr looseHwnd = LooseHandle();
        foreach (var fence in _manager.Fences.ToList())
        {
            var fw = new FenceWindow(fence, _manager, this, looseHwnd);
            _fenceWindows.Add(fw);
            fw.Show();
        }

        ReorderZ();
    }

    private IntPtr LooseHandle() =>
        _looseLayer is null ? IntPtr.Zero : new WindowInteropHelper(_looseLayer).Handle;

    private void RefreshAll()
    {
        _looseLayer?.Rebuild();
        foreach (var fw in _fenceWindows) fw.Rebuild();
    }

    /// <summary>Keep the loose layer at the very bottom, fences just above it.</summary>
    private void ReorderZ()
    {
        foreach (var fw in _fenceWindows) fw.SinkToBottom();
        _looseLayer?.SinkToBottom();
    }

    private void CloseAllWindows()
    {
        foreach (var fw in _fenceWindows.ToList()) fw.Close();
        _fenceWindows.Clear();
        _looseLayer?.Close();
        _looseLayer = null;
    }

    // ---- IDesktopCoordinator ---------------------------------------------

    public IEnumerable<DesktopItem> LooseItems() =>
        _items.Values.Where(i => !_manager.IsFenced(i.Path));

    public IEnumerable<DesktopItem> MembersOf(Fence fence)
    {
        foreach (var path in fence.MemberPaths)
            if (_items.TryGetValue(path, out var item))
                yield return item;
    }

    public void DropToFence(string path, Fence fence)
    {
        _manager.Assign(path, fence);
        RefreshAll();
    }

    public void DropToLoose(string path, double canvasX, double canvasY)
    {
        _manager.RemoveFromFences(path);
        _looseStore.Set(path, canvasX, canvasY);
        RefreshAll();
    }

    public void BeginDrag() => _looseLayer?.SetDropMode(true);
    public void EndDrag() => _looseLayer?.SetDropMode(false);

    public void DeleteFence(Fence fence)
    {
        _manager.RemoveFence(fence);
        var fw = _fenceWindows.FirstOrDefault(w => w.Fence == fence);
        if (fw is not null)
        {
            _fenceWindows.Remove(fw);
            fw.Close();
        }
        RefreshAll();
    }

    // ---- Tray -------------------------------------------------------------

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New fence", null, (_, _) => NewFence());
        menu.Items.Add("Rescan desktop", null, (_, _) => Rescan());
        menu.Items.Add("Show/hide real desktop icons", null, (_, _) => ToggleRealIcons());

        var startup = new Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = IsRunAtLogin(),
            CheckOnClick = true,
        };
        startup.Click += (_, _) => SetRunAtLogin(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Shimmer",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => NewFence();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var s = typeof(App).Assembly.GetManifestResourceStream("Shimmer.Assets.shimmer.ico");
            if (s is not null) return new Drawing.Icon(s);
        }
        catch { /* fall back below */ }
        return Drawing.SystemIcons.Application;
    }

    // ---- Run at login (HKCU Run key) -------------------------------------

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Shimmer";

    private static bool IsRunAtLogin()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private static void SetRunAtLogin(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (on)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch { /* registry access denied etc.: non-fatal */ }
    }

    private void NewFence()
    {
        if (!_iconsHidden) return;
        var area = SystemParameters.WorkArea;
        double left = area.Left + 80 + _fenceWindows.Count * 24;
        double top = area.Top + 80 + _fenceWindows.Count * 24;

        var fence = _manager.CreateFence(left, top, 320, 240);
        var fw = new FenceWindow(fence, _manager, this, LooseHandle());
        _fenceWindows.Add(fw);
        fw.Show();
        ReorderZ();
    }

    private void Rescan()
    {
        if (_iconsHidden) HideRealIconsAndRender();
    }

    private void ToggleRealIcons()
    {
        if (_iconsHidden)
        {
            _iconsHidden = false;
            CloseAllWindows();
            RealDesktopIcons.Show();
        }
        else
        {
            HideRealIconsAndRender();
        }
    }

    // ---- Lifetime ---------------------------------------------------------

    private void ExitApp()
    {
        _hideWatch?.Stop();
        RestoreRealIcons();
        _tray.Visible = false;
        _tray.Dispose();
        Shutdown();
    }

    private void RestoreRealIcons()
    {
        try
        {
            CloseAllWindows();
            RealDesktopIcons.Show();
            _iconsHidden = false;
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RestoreRealIcons();
        base.OnExit(e);
    }
}
