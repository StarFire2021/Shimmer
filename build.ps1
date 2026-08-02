<#
    Shimmer — one-command build & package.

    Publishes a self-contained, single-file Release build, then (if Inno Setup is
    installed) compiles the installer.

    Usage (from the repo root):
        powershell -ExecutionPolicy Bypass -File .\build.ps1

    Options:
        -PublishDir   Where the published exe goes. MUST match the PublishDir
                      defined in installer\shimmer.iss (default: Documents\Shimmer).
        -SelfContained  $true (bundle the .NET runtime, no prerequisites) or
                        $false (smaller build; user needs the .NET 8 Desktop Runtime).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [string]$PublishDir = "$env:USERPROFILE\Documents\Shimmer"
)

$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$project = Join-Path $root "src\Shimmer\Shimmer.csproj"
$iss     = Join-Path $root "installer\shimmer.iss"
$scArg   = if ($SelfContained) { "true" } else { "false" }

Write-Host "==> Publishing Shimmer ($Configuration, $Runtime, self-contained=$scArg)" -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $scArg `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
Write-Host "    Published to $PublishDir" -ForegroundColor Green

# --- Locate the Inno Setup compiler (ISCC.exe) ---
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $iscc) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found — skipping the installer step."
    Write-Host   "Install it from https://jrsoftware.org/isdl.php, then re-run to build the installer." -ForegroundColor Yellow
    return
}

Write-Host "==> Building installer with $iscc" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

$setup = Get-ChildItem (Join-Path $root "installer\Output\Shimmer-Setup-*.exe") |
         Sort-Object LastWriteTime | Select-Object -Last 1
if ($setup) {
    Write-Host "==> Done. Installer: $($setup.FullName)" -ForegroundColor Green
} else {
    Write-Host "==> Publish done; installer output not found (check Inno Setup output)." -ForegroundColor Yellow
}
