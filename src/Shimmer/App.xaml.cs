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
/// own: a loose layer for un-pooled items and a window per pool. Implements
/// <see cref="IDesktopCoordinator"/> so tiles, the loose layer, and pools can
/// resolve membership and apply drops through one brain.
/// </summary>
public partial class App : System.Windows.Application, IDesktopCoordinator
{
    private Forms.NotifyIcon _tray = null!;
    private LooseStore _looseStore = null!;
    private UsageStore _usage = null!;
    private GlobalStore _globalStore = null!;
    private GlobalSettings _global = new();
    private PoolManager _manager = null!;
    private LooseLayerWindow? _looseLayer;
    private readonly List<PoolWindow> _poolWindows = new();
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
        _usage = new UsageStore();
        _globalStore = new GlobalStore();
        _global = _globalStore.Load();
        _manager = new PoolManager(new PoolStore());
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
        foreach (var pool in _manager.Pools.ToList())
        {
            var pw = new PoolWindow(pool, _manager, this, looseHwnd);
            _poolWindows.Add(pw);
            pw.Show();
        }

        ReorderZ();
    }

    private IntPtr LooseHandle() =>
        _looseLayer is null ? IntPtr.Zero : new WindowInteropHelper(_looseLayer).Handle;

    private void RefreshAll()
    {
        _looseLayer?.Rebuild();
        foreach (var pw in _poolWindows) pw.Rebuild();
    }

    /// <summary>Keep the loose layer at the very bottom, pools just above it.</summary>
    private void ReorderZ()
    {
        foreach (var pw in _poolWindows) pw.SinkToBottom();
        _looseLayer?.SinkToBottom();
    }

    private void CloseAllWindows()
    {
        foreach (var pw in _poolWindows.ToList()) pw.Close();
        _poolWindows.Clear();
        _looseLayer?.Close();
        _looseLayer = null;
    }

    // ---- IDesktopCoordinator ---------------------------------------------

    public IEnumerable<DesktopItem> LooseItems() =>
        _items.Values.Where(i => !_manager.IsPooled(i.Path));

    public IEnumerable<DesktopItem> MembersOf(Pool pool)
    {
        var ordered = new List<DesktopItem>(pool.MemberPaths.Count);
        foreach (var path in pool.MemberPaths)
            if (_items.TryGetValue(path, out var item))
                ordered.Add(item);
        return MemberSorter.Sort(ordered, pool.SortMode, _usage.Snapshot());
    }

    public void DropToPoolAt(string path, Pool pool, int index)
    {
        // Bake the currently displayed order into MemberPaths (so what you see
        // becomes the manual baseline), then insert the dropped item at the spot.
        var order = MembersOf(pool).Select(i => i.Path).ToList();
        order.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _manager.RemoveFromPools(path);

        index = Math.Clamp(index, 0, order.Count);
        order.Insert(index, path);

        pool.MemberPaths = order;
        pool.SortMode = SortMode.Manual;
        _manager.Save();
        RefreshAll();
    }

    public void DropToLoose(string path, double canvasX, double canvasY)
    {
        _manager.RemoveFromPools(path);
        _looseStore.Set(path, canvasX, canvasY);
        RefreshAll();
    }

    public void BeginDrag() => _looseLayer?.SetDropMode(true);
    public void EndDrag() => _looseLayer?.SetDropMode(false);

    public void RecordLaunch(string path) => _usage.Record(path);

    public void DeletePool(Pool pool)
    {
        _manager.RemovePool(pool);
        var pw = _poolWindows.FirstOrDefault(w => w.Pool == pool);
        if (pw is not null)
        {
            _poolWindows.Remove(pw);
            pw.Close();
        }
        RefreshAll();
    }

    // ---- Tray -------------------------------------------------------------

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New pool", null, (_, _) => NewPool());
        menu.Items.Add(BuildAllPoolsMenu());
        menu.Items.Add(new Forms.ToolStripSeparator());
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
        _tray.DoubleClick += (_, _) => NewPool();
    }

    /// <summary>
    /// The "Global Settings" submenu: each choice sets the global default and
    /// applies it to every (unlocked) pool. Check marks reflect the current global
    /// setting (refreshed when the submenu opens).
    /// </summary>
    private Forms.ToolStripMenuItem BuildAllPoolsMenu()
    {
        var root = new Forms.ToolStripMenuItem("Global Settings");

        // Sort
        var sort = new Forms.ToolStripMenuItem("Sort by");
        foreach (var (label, mode) in new (string, SortMode)[]
        {
            ("Manual (drag to arrange)", SortMode.Manual), ("Name", SortMode.Name),
            ("Type", SortMode.Type), ("Date modified", SortMode.DateModified),
            ("Recently used", SortMode.RecentlyUsed),
        })
        {
            var m = mode;
            var it = new Forms.ToolStripMenuItem(label) { Tag = mode };
            it.Click += (_, _) => { _global.SortMode = m; ApplyToAllPools(p => p.SortMode = m); };
            sort.DropDownItems.Add(it);
        }
        root.DropDownItems.Add(sort);

        // Title alignment
        var title = new Forms.ToolStripMenuItem("Title alignment");
        foreach (var (label, align) in new (string, TitleAlign)[]
        {
            ("Left", TitleAlign.Left), ("Center", TitleAlign.Center), ("Right", TitleAlign.Right),
        })
        {
            var a = align;
            var it = new Forms.ToolStripMenuItem(label) { Tag = align };
            it.Click += (_, _) => { _global.TitleAlign = a; ApplyToAllPools(p => p.TitleAlign = a); };
            title.DropDownItems.Add(it);
        }
        root.DropDownItems.Add(title);

        // Snapping (mutually exclusive)
        var snap = new Forms.ToolStripMenuItem("Snapping");
        var snapNone = new Forms.ToolStripMenuItem("None");
        snapNone.Click += (_, _) => { _global.AutoSnap = false; _global.MagneticSnap = false;
            ApplyToAllPools(p => { p.AutoSnap = false; p.MagneticSnap = false; }); };
        var snapAuto = new Forms.ToolStripMenuItem("Auto-size to icon grid");
        snapAuto.Click += (_, _) => { _global.AutoSnap = true; _global.MagneticSnap = false;
            ApplyToAllPools(p => { p.AutoSnap = true; p.MagneticSnap = false; }); };
        var snapMag = new Forms.ToolStripMenuItem("Snap edges to even padding");
        snapMag.Click += (_, _) => { _global.MagneticSnap = true; _global.AutoSnap = false;
            ApplyToAllPools(p => { p.MagneticSnap = true; p.AutoSnap = false; }); };
        snap.DropDownItems.AddRange(new Forms.ToolStripItem[] { snapNone, snapAuto, snapMag });
        root.DropDownItems.Add(snap);

        // Appearance (opens the color/opacity picker, applies to all pools)
        root.DropDownItems.Add("Appearance…", null, (_, _) =>
        {
            if (PoolWindow.ShowAppearanceDialog(_global.Color, _global.Opacity, out var color, out var opacity))
            {
                _global.Color = color;
                _global.Opacity = opacity;
                ApplyToAllPools(p => { p.Color = color; p.Opacity = opacity; });
            }
        });

        root.DropDownItems.Add(new Forms.ToolStripSeparator());

        // Roll-up is a bulk command (not a stored default).
        root.DropDownItems.Add("Roll all up", null, (_, _) => ApplyToAllPools(p => p.RolledUp = true));
        root.DropDownItems.Add("Roll all down", null, (_, _) => ApplyToAllPools(p => p.RolledUp = false));

        root.DropDownItems.Add(new Forms.ToolStripSeparator());

        // Toggles
        var menuBtn = new Forms.ToolStripMenuItem("Show menu button") { CheckOnClick = true };
        menuBtn.Click += (_, _) => { _global.ShowMenuButton = menuBtn.Checked;
            ApplyToAllPools(p => p.ShowMenuButton = _global.ShowMenuButton); };
        root.DropDownItems.Add(menuBtn);

        var hideHover = new Forms.ToolStripMenuItem("Hide until hover") { CheckOnClick = true };
        hideHover.Click += (_, _) => { _global.AutoHide = hideHover.Checked;
            ApplyToAllPools(p => p.AutoHide = _global.AutoHide); };
        root.DropDownItems.Add(hideHover);

        // Reflect current global settings whenever the submenu opens.
        root.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripItem i in sort.DropDownItems)
                if (i is Forms.ToolStripMenuItem mi && mi.Tag is SortMode sm) mi.Checked = sm == _global.SortMode;
            foreach (Forms.ToolStripItem i in title.DropDownItems)
                if (i is Forms.ToolStripMenuItem mi && mi.Tag is TitleAlign ta) mi.Checked = ta == _global.TitleAlign;
            snapNone.Checked = !_global.AutoSnap && !_global.MagneticSnap;
            snapAuto.Checked = _global.AutoSnap;
            snapMag.Checked = _global.MagneticSnap;
            menuBtn.Checked = _global.ShowMenuButton;
            hideHover.Checked = _global.AutoHide;
        };

        return root;
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

    private void NewPool()
    {
        if (!_iconsHidden) return;
        var area = SystemParameters.WorkArea;
        double left = area.Left + 80 + _poolWindows.Count * 24;
        double top = area.Top + 80 + _poolWindows.Count * 24;

        var pool = _manager.CreatePool(left, top, 320, 240);
        _global.ApplyTo(pool); // new pools inherit the global defaults
        _manager.Save();

        var pw = new PoolWindow(pool, _manager, this, LooseHandle());
        _poolWindows.Add(pw);
        pw.Show();
        ReorderZ();
    }

    // ---- Global (all-pool) settings --------------------------------------

    /// <summary>Apply a single property change to every pool and persist, then
    /// re-render the pool windows so it shows immediately. Only the given property
    /// is touched, so other per-pool customizations are left alone.</summary>
    private void ApplyToAllPools(Action<Pool> set)
    {
        foreach (var p in _manager.Pools)
            if (!p.Locked) set(p); // locked pools opt out of global changes
        _manager.Save();
        _globalStore.Save(_global);
        RecreatePoolWindows();
    }

    private void RecreatePoolWindows()
    {
        if (!_iconsHidden) return;
        IntPtr looseHwnd = LooseHandle();
        foreach (var pw in _poolWindows.ToList()) pw.Close();
        _poolWindows.Clear();
        foreach (var pool in _manager.Pools.ToList())
        {
            var pw = new PoolWindow(pool, _manager, this, looseHwnd);
            _poolWindows.Add(pw);
            pw.Show();
        }
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
