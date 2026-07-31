#define MyAppName "Suno Pesme Studio"
#define MyAppVersion "3.2.0"
#define MyAppPublisher "Nedostaješ PUNOO"
#define MyAppExeName "POKRENI_PROGRAM.bat"

[Setup]
AppId={{7D92E2F4-2EF2-42B3-A462-D1919C6E1800}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Suno Pesme Studio
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=Suno-Pesme-Studio-v3.2.0-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\POKRENI_PROGRAM.bat
VersionInfoVersion=3.2.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Files]
Source: "..\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "data\*;Preuzete_pesme\*;Izvoz\*;Ažuriranja\*;Azuriranja\*;installer\output\*;*.pyc;__pycache__\*;*.part;*.tmp"

[Dirs]
Name: "{app}\data"; Permissions: users-modify
Name: "{app}\Preuzete_pesme"; Permissions: users-modify
Name: "{app}\Izvoz"; Permissions: users-modify
Name: "{app}\Azuriranja"; Permissions: users-modify

[Icons]
Name: "{group}\Suno Pesme Studio"; Filename: "{cmd}"; Parameters: "/c ""{app}\POKRENI_PROGRAM.bat"""; WorkingDir: "{app}"
Name: "{group}\Alati i održavanje"; Filename: "{sys}\explorer.exe"; Parameters: """{app}\ALATI_I_ODRZAVANJE"""
Name: "{autodesktop}\Suno Pesme Studio"; Filename: "{cmd}"; Parameters: "/c ""{app}\POKRENI_PROGRAM.bat"""; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Napravi ikonu na radnoj površini"; GroupDescription: "Dodatne ikone:"

[Run]
Filename: "{cmd}"; Parameters: "/c ""{app}\POKRENI_PROGRAM.bat"""; WorkingDir: "{app}"; Description: "Pokreni Suno Pesme Studio"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c ""{app}\ALATI_I_ODRZAVANJE\ZAUSTAVI_PROGRAM.bat"""; WorkingDir: "{app}"; Flags: runhidden

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c "' + ExpandConstant('{app}\ALATI_I_ODRZAVANJE\ZAUSTAVI_PROGRAM.bat') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    MsgBox('Lična biblioteka, preuzete pesme, Izvoz i backup podaci neće biti namerno obrisani. Možeš ih ručno sačuvati ili obrisati posle deinstalacije.', mbInformation, MB_OK);
end;
