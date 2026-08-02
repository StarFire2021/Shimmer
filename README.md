# Shimmer

A custom, Fences-style desktop organizer for Windows. Shimmer groups your real
desktop icons into labeled boxes ("fences") and hides a fence's icons until you
hover over it. It is a from-scratch C#/.NET app that talks directly to the
Windows shell — no third-party runtime, no browser.

This is **v1**: the two features you asked for — *group into fences* and *hide
until hover* — working against your actual desktop icons.

---

## ⚠️ Architecture pivot in progress (v2)

The original approach *moved* real desktop icons via Win32. That fights Explorer,
which snaps icons back to its own saved layout on clicks/refresh. We are pivoting
to the way Fences actually works: **hide Explorer's real icon layer entirely and
render our own tiles** (files never move; it's a reversible display hide).

- **Phase 1 (current):** the new engine — hide the real icons, scan the desktop,
  extract each item's real icon, and render un-fenced items as a draggable,
  double-click-to-launch **loose tile layer**. Fence windows are not spawned in
  this phase (the fence code remains, to be rebuilt on tiles).
- **Phase 2:** fences return as tile containers (drag tiles in, whole-fence
  hide-until-hover, reusing color/opacity + backdrop).
- **Phase 3:** system icons (Recycle Bin, This PC), multi-monitor polish,
  run-at-login.

Everything below the line describes the original v1 icon-moving model and is being
superseded phase by phase.

---

## What it does today

- **Fences.** Create labeled rectangles anywhere on the desktop from the tray
  icon. Drag them by their title bar. Rename or delete via right-click.
- **Capture real icons.** Right-click a fence's title bar → *Capture icons in
  this area*. Every desktop icon whose center sits inside the fence becomes a
  member and gets tidied into a grid.
- **Hide until hover.** Click the chevron (or right-click → *Roll up*) to roll a
  fence up: its member icons are parked far off-screen so the desktop looks
  clean, leaving just the title bar. Hover the title bar and the icons snap back
  into view; move the mouse away and they hide again.
- **Solid tinted backdrop.** Each open fence shows a colored panel behind its
  icons. Right-click the title bar → *Appearance…* to pick a color (swatches or
  hex) and set opacity. The panel is a separate click-through window, so your
  icons still open normally through it.
- **Resize.** Drag the right edge, bottom edge, or bottom-right corner of an open
  fence; icons reflow into the new size and follow the fence when you move it.
- **Persistence.** Fences, their members, positions, color, and opacity are saved to
  `%AppData%\Shimmer\fences.json` and restored on the next launch.
- **Tray control.** New fence, Reveal all, Save now, Exit.

---

## Build & run

You need the **.NET 8 SDK** (or just open the solution in **Visual Studio 2022**,
which will offer to install it). WPF is Windows-only, so this must be built on
Windows.

### Visual Studio
1. Open `Shimmer.sln`.
2. Set the build to **x64** (already the project default — see note below).
3. Press **F5**.

### Command line
```powershell
cd "C:\Users\Phoenix\Documents\VS Code\Shimmer"
dotnet build -c Release
dotnet run --project src\Shimmer\Shimmer.csproj -c Release
```

Shimmer starts minimized to the system tray (look for the notification-area
icon). **Double-click the tray icon** or use its menu to create your first
fence.

> **Why x64?** Shimmer reads and writes memory inside `explorer.exe` to move
> desktop icons (see architecture below). Pointer sizes must match, and modern
> explorer is 64-bit, so the project forces `PlatformTarget=x64`. If you ever run
> a 32-bit explorer you'd need an x86 build.

---

## How it works (architecture)

The desktop icons are not separate windows — they are items in a single
`SysListView32` control owned by `explorer.exe`. That one fact drives the whole
design.

```
Progman ─ SHELLDLL_DefView ─ SysListView32 ("FolderView")   ← the desktop icons
```

To move an icon you send the ListView a message like `LVM_SETITEMPOSITION32`.
The catch: these messages take a *pointer*, and that pointer is dereferenced
inside explorer's address space, not yours. So Shimmer:

1. Finds the desktop ListView handle (`Interop/DesktopListView.FindDesktopListView`).
2. `OpenProcess` on explorer and `VirtualAllocEx` a small scratch buffer *inside*
   explorer.
3. For a read (position/label): sends the message, then `ReadProcessMemory` pulls
   explorer's reply back out.
4. For a write (move): `WriteProcessMemory` pokes the coordinates in, then sends
   the message.

That cross-process dance lives entirely in `Interop/DesktopListView.cs`;
`Interop/NativeMethods.cs` holds the raw P/Invoke declarations.

Everything else is ordinary app code:

| Layer | File | Responsibility |
|-------|------|----------------|
| Interop | `Interop/NativeMethods.cs` | P/Invoke surface (Win32) |
| Interop | `Interop/DesktopListView.cs` | Read/move real desktop icons across processes |
| Model | `Models/DesktopIcon.cs`, `Models/Fence.cs` | Data |
| Storage | `Services/FenceStore.cs` | JSON load/save to `%AppData%\Shimmer` |
| Logic | `Services/FenceManager.cs` | Membership, hide/reveal, grid arrangement |
| UI | `Windows/FenceWindow.xaml(.cs)` | The visible container + hover logic |
| Shell | `App.xaml(.cs)` | Tray icon, app lifetime |

### How the backdrop works (and its one caveat)

Real Fences draws its translucent boxes *underneath* the icons, which requires
compositing into the desktop's wallpaper host (`WorkerW`) — genuinely deep shell
work. Shimmer takes a pragmatic route that gets the same look without it: each
fence owns a **second, click-through window** (`BackdropWindow`) painted with the
chosen color/opacity and pinned just beneath the fence's chrome. It carries the
`WS_EX_TRANSPARENT` style, so mouse clicks fall straight through the tint to the
real icons underneath — the icons stay fully clickable and simply appear to sit
on a colored glass panel.

The caveat versus true Fences: because the panel sits *over* the icons (tinting
them) rather than *behind* them, icons read as slightly color-shifted rather than
full-brightness on a solid box. If you ever want the exact behind-the-icons look,
that's still the `WorkerW` compositing route — a larger future change.

---

## Roadmap ideas

- Solid tinted backdrop rendered behind icons (WorkerW compositing).
- Resize handles on fences.
- Auto-organize rules (sort new icons by type into fences).
- Double-click empty desktop to toggle all icons.
- Snapshot/restore desktop layouts.
- Run at login (registry `Run` key or Startup shortcut).

## Troubleshooting

- **Nothing happens on "Capture".** Make sure the fence overlaps icons and that
  *Align icons to grid* / *Auto arrange* is off in the desktop right-click menu,
  otherwise explorer may snap icons back.
- **Icons don't move / access denied.** If you run explorer elevated, run Shimmer
  elevated too (change `app.manifest` to `requireAdministrator`).
- **Fence sits over icons oddly.** Drag it by the title bar; geometry is saved on
  release.
