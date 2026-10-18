; Inno Setup script for Shimmer
; -----------------------------------------------------------------------------
; Build the payload first (see PACKAGING.md), then open this file in Inno Setup
; (https://jrsoftware.org/isdl.php) and click Build > Compile. It produces a
; single Shimmer-Setup-x.y.z.exe in the "Output" folder next to this script.
;
; This installs PER USER (into %LocalAppData%\Programs\Shimmer), so it needs no
; administrator rights — a good fit for a tray utility.

#define MyAppName "Shimmer"
; MyAppVersion and PublishDir can be overridden on the command line
; (ISCC /DMyAppVersion=1.2.3 /DPublishDir=...), which the GitHub Actions
; workflow uses. Local builds just use the defaults below.
#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#define MyAppPublisher "Steven"
#define MyAppExeName "Shimmer.exe"
; Path to the published output (win-x64). Adjust if you publish elsewhere.
#ifndef PublishDir
  #define PublishDir "C:\Users\Phoenix\Documents\Shimmer\"
#endif

[Setup]
AppId={{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Shimmer-Setup-{#MyAppVersion}
SetupIconFile=..\src\Shimmer\Assets\shimmer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Make sure a running Shimmer is closed before install/uninstall, so it can
; restore the real desktop icons cleanly.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} automatically when Windows starts"; GroupDescription: "Startup:"

[Files]
; Copies the whole publish folder, so this works whether you publish as a single
; self-contained exe or as framework-dependent (multiple files).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent
