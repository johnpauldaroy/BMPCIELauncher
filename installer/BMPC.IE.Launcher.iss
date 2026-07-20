; Inno Setup script for BMPC IE Launcher.
; Packages the self-contained .NET 8 publish output into a single setup.exe.
; The published app already bundles the .NET 8 runtime, so target PCs need nothing pre-installed.

#define MyAppName "BMPC IE Launcher"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Barbaza Multi-Purpose Cooperative"
#define MyAppExeName "BMPC.LegacyEdgeLauncher.exe"
; Folder produced by `dotnet publish` (self-contained, single-file win-x64).
#define PublishDir "..\publish\Desktop"

[Setup]
; A unique AppId keeps upgrades/uninstalls tied to this product across versions.
AppId={{7C4B9F2A-3D6E-4A1B-9E2C-BMPCIELAUNCH01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BMPC\IE Launcher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Installing into Program Files requires elevation; the app itself still runs as-invoker.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=BMPC-IE-Launcher-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
; Branding icon for the setup.exe itself and the wizard.
SetupIconFile=..\src\BMPC.LegacyEdgeLauncher.Desktop\Assets\coop.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Deploy the entire self-contained publish folder. The native WPF/SQLite DLLs must
; sit next to the executable, so we take everything the publish step produced
; (excluding debug symbols and XML docs, which are not needed at runtime).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
