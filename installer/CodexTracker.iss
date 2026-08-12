; Per-user installer. Configuration in %APPDATA% is intentionally retained on uninstall.
#define AppName "Codex Tracker"
#define AppVersion "0.4.7"
#define AppPublisher "Codex Tracker"
#define AppExeName "CodexTracker.exe"

[Setup]
AppId={{D8C84F82-ED90-4F1F-AB4E-1455E5B66C2C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=CodexTracker-Setup-{#AppVersion}
SetupIconFile=..\assets\brand\codex-tracker.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start Codex Tracker when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Codex Tracker"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Codex Tracker"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexTracker"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Codex Tracker"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--shutdown-existing"; Flags: runhidden waituntilterminated; RunOnceId: "ShutdownCodexTracker"
