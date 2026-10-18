<div align="center">

<img src="src/Shimmer/Assets/shimmer_preview.png" width="96" alt="Shimmer icon" />

# Shimmer

**Tidy your Windows desktop into translucent, water-themed "pools."**

Shimmer hides your real desktop icons and re-renders them as its own tiles, which
you can group into labeled, colored **pools** — draggable containers that can hide
until you hover them. A desktop icon organizer, built from scratch in C#/.NET WPF.

**[⬇ Download the latest release](https://github.com/StarFire2021/Shimmer/releases/latest)**

[![Build & Release](https://github.com/StarFire2021/Shimmer/actions/workflows/release.yml/badge.svg)](https://github.com/StarFire2021/Shimmer/actions/workflows/release.yml)

</div>

---

> _Add a screenshot here once you have one — e.g. `docs/screenshot.png` showing a
> couple of colored pools on the desktop._

## What it does

- **Pools.** Create labeled, tinted containers from the tray. Drag desktop tiles
  into a pool to group them; drag them back out to release them to the desktop.
- **Hide until hover.** Toggle a pool to vanish completely — title bar and all —
  keeping your desktop clean, then reappear when your cursor moves over its spot.
- **Roll up.** Collapse a pool to just its title bar without hiding it.
- **Real icons, real behavior.** Tiles show each item's actual Windows icon,
  **double-click to launch**, and **right-click for the genuine shell menu**
  (Open, Copy, Delete, Rename, Properties, …).
- **Appearance.** Give each pool a color with a visual color picker (hue strip +
  saturation/brightness field) and an opacity slider.
- **Resize & arrange.** Drag pool edges to resize; drag tiles anywhere on the
  desktop. Positions, membership, colors, and layout persist across restarts.
- **Stays out of the way.** Pools sit on the desktop *behind* your app windows,
  never stealing focus, with a custom tray icon and optional run-at-login.
- **Self-updating.** "Check for updates" in the tray grabs the latest release from
  GitHub and runs the installer for you.

## How it works

Windows desktop icons are all items inside a single `SysListView32` owned by
`explorer.exe`, and Explorer insists on managing their positions — so *moving*
them from another process is a losing battle (Explorer snaps them back). Shimmer
takes a different approach: it **hides Explorer's entire icon layer** (a reversible
display hide — no files are moved or modified) and **renders its own tiles**
instead. Un-pooled items live on a full-desktop "loose" layer;
pooled items live in their pool windows. When Shimmer isn't running, your normal
desktop icons come right back, untouched.

A fuller architecture tour lives in the source comments; the interesting bits are
`Interop/` (shell + Win32), `Windows/` (the loose layer and pool windows), and
`Services/` (scanning, membership, persistence).

## Requirements

- Windows 10 or 11 (x64)
- To build: **.NET 8 SDK** and **Visual Studio 2022** (or `dotnet` CLI)
- To run a published build: nothing extra if published self-contained; otherwise
  the **.NET 8 Desktop Runtime**

## Build & run

```powershell
git clone https://github.com/StarFire2021/Shimmer.git
cd Shimmer
dotnet run --project src/Shimmer/Shimmer.csproj -c Release
```

Shimmer starts in the system tray. Right-click the tray icon → **New pool**, then
drag tiles into it. To stop, use the tray's **Exit** (which restores your real
desktop icons).

Packaging into an installer (Inno Setup script included) is documented in
[PACKAGING.md](PACKAGING.md).

## Roadmap

- System/namespace icons (Recycle Bin, This PC) as tiles
- Multi-monitor layout polish
- Auto-organize rules (sort new items into pools by type)
- Snapshot / restore desktop layouts

## Notes & limitations

- While Shimmer is stopped, the desktop shows your **normal** Windows icons rather
  than the pooled layout — nothing is ever lost; hiding is just a display toggle.
- System icons that aren't files (Recycle Bin, This PC) aren't rendered as tiles
  yet (see Roadmap).

## License

Released under the [MIT License](LICENSE).
