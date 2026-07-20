; Per-user update installer for BMPC IE Launcher.
; Installs to the current user's AppData with NO admin/UAC, so the in-app auto-updater
; can apply updates silently on standalone units. Machine-wide Edge/IE policies are NOT
; touched here — those are applied once by the admin installer at first setup.

#define MyAppName "BMPC IE Launcher"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Barbaza Multi-Purpose Cooperative"
#define MyAppExeName "BMPC.LegacyEdgeLauncher.exe"
#define PublishDir "..\publish\Desktop"

[Setup]
; Same AppId family, distinct per-user context. Keeps its own uninstall entry per user.
AppId={{7C4B9F2A-3D6E-4A1B-9E2C-BMPCIELAUNCH01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install location — no elevation required.
DefaultDirName={localappdata}\Programs\BMPC\IE Launcher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=BMPC-IE-Launcher-Update-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\BMPC.LegacyEdgeLauncher.Desktop\Assets\coop.ico
; Close the running app so its files can be replaced, and reopen it after.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; Relaunch the app after updating. WizardSilent() is a built-in Inno function that is true
; during the silent (auto-updater) install, so the app reopens in both interactive and silent flows.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent
