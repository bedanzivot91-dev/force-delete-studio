; NP Suno Unified Studio - one installer for both fully embedded modules.
#define MyAppName "NP Suno Unified Studio"
#define MyAppVersion "3.3.2"
#define MyAppPublisher "NP Suno Unified Studio"
#define MyAppExeName "NP Suno Unified Studio.exe"
#define MyProjectFileExt ".npvsproject"

[Setup]
AppId={{E2C79E27-8A2E-4B3C-93D2-43A2B68F9321}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
OutputDir=..\unified_dist
OutputBaseFilename=NP-Suno-Unified-Studio-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Napravi prečicu na Radnoj površini"; GroupDescription: "Prečice:"; Flags: checkedonce
Name: "associate"; Description: "Poveži .npvsproject projekte sa ugrađenim NP Video Studio modulom"; GroupDescription: "Registracija fajlova:"; Flags: checkedonce

[Files]
Source: "..\unified_publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Deinstaliraj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\{#MyProjectFileExt}"; ValueType: string; ValueName: ""; ValueData: "NPVideoStudioProject"; Tasks: associate; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject"; ValueType: string; ValueName: ""; ValueData: "NP Video Studio projekat"; Tasks: associate; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Modules\Video\NPVideoStudio.exe,0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Modules\Video\NPVideoStudio.exe"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Pokreni {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ask the Suno loopback backend to stop cleanly before installed files are removed.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -WindowStyle Hidden -Command ""try {{ Invoke-WebRequest -UseBasicParsing -Method POST -Uri 'http://127.0.0.1:8765/api/shutdown' -TimeoutSec 2 | Out-Null }} catch {{ }}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ""NPVideoStudio.exe"" /T"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ""Suno Pesme Studio.exe"" /T"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ""NP Suno Unified Studio.exe"" /T"; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
; User projects, downloaded songs and application data live outside {app} and are deliberately preserved.
Type: filesandordirs; Name: "{app}"
