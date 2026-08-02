# Packaging Shimmer as an installable app

Two stages: **publish** the app into a distributable folder, then **package** that
folder into an installer.

## Quick way (one command)

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` publishes a self-contained single-file build and then compiles the
installer (if Inno Setup is installed). The manual stages below explain what it
does and how to do it by hand.

## Stage 1 — Publish (Visual Studio)

You want a self-contained, single-file build so users don't need to install the
.NET 8 Desktop Runtime separately.

**In Visual Studio:**
1. Right-click the **Shimmer** project → **Publish…**
2. Target: **Folder** → Next.
3. On the profile summary click **Show all settings** and set:
   - Configuration: **Release**
   - Target framework: **net8.0-windows**
   - Deployment mode: **Self-contained**
   - Target runtime: **win-x64**
   - Produce single file: **checked**
   - (Optional) Enable compression, ReadyToRun for faster startup.
   - Leave **trimming OFF** — trimming is not reliable for WPF and can break it.
4. **Publish**. Output lands in
   `src\Shimmer\bin\Release\net8.0-windows\win-x64\publish\`.

**Or via the command line:**
```powershell
dotnet publish src\Shimmer\Shimmer.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

Self-contained is ~100–150 MB because it bundles the runtime. If you'd rather
ship a small (~1 MB) build and require users to have the **.NET 8 Desktop
Runtime**, drop `--self-contained true` (use `--self-contained false`).

## Stage 2 — Package (installer)

**Recommended: Inno Setup** (free, simple, produces one `Setup.exe`). A ready
script is in `installer\shimmer.iss`.
1. Install Inno Setup: https://jrsoftware.org/isdl.php
2. Open `installer\shimmer.iss`, bump `MyAppVersion` if needed.
3. **Build → Compile**. Output: `installer\Output\Shimmer-Setup-1.0.0.exe`.

The script installs per-user (no admin), adds a Start Menu shortcut, offers a
"start with Windows" checkbox, sets the Shimmer icon, and closes a running
instance before install/uninstall so the real desktop icons are restored cleanly.

### Other options
- **WiX Toolset** → a classic `.msi`. More powerful, steeper learning curve.
- **ClickOnce** (built into VS Publish) → easy + auto-update, but weaker for a
  tray/desktop-integration app.
- **MSIX** → modern, but **not recommended for Shimmer**: MSIX runs the app with
  packaged identity in a partially virtualized/sandboxed environment, which can
  interfere with what Shimmer does — cross-process reads/writes into
  `explorer.exe`, hiding the desktop icon window, and the `HKCU\…\Run` autostart
  key (registry writes get virtualized). A classic installer avoids all of that.

## Troubleshooting

**"Unable to find package Microsoft.NETCore.App.Runtime.win-x64" (or
WindowsDesktop/AspNetCore) on publish.** A self-contained publish downloads the
runtime as NuGet "runtime packs," but your machine's NuGet has no online source
for them. Fixed by the `NuGet.config` at the repo root (adds nuget.org). If it
still fails:
- Confirm the machine has internet access to `api.nuget.org`.
- Or add nuget.org in VS: **Tools → Options → NuGet Package Manager →
  Package Sources → +**, Source `https://api.nuget.org/v3/index.json`.
- Or sidestep runtime packs entirely with a **framework-dependent** publish
  (uncheck "Self-contained"; users then need the .NET 8 Desktop Runtime):
  ```powershell
  dotnet publish src\Shimmer\Shimmer.csproj -c Release -r win-x64 --self-contained false
  ```

## Notes specific to Shimmer
- **No admin needed at runtime.** The manifest is `asInvoker`; a per-user install
  keeps the whole flow admin-free.
- **Autostart** already works from the tray ("Start with Windows"), which writes
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` pointing at wherever the
  exe is installed. The installer also offers a Startup-folder shortcut.
- **Icon caching.** Explorer caches exe icons; if the old icon lingers after
  installing, it's the Windows icon cache, not the build.
- **Code signing (optional but recommended).** Unsigned installers trigger a
  SmartScreen warning on first run. A code-signing certificate (e.g. from a CA,
  or a cheaper OV cert) signed onto both `Shimmer.exe` and the setup `.exe`
  removes it.
