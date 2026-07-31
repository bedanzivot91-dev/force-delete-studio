; NP Video Studio - Inno Setup instalaciona skripta.
;
; NAPOMENA: Ovaj skript je napisan i ide uz izvorni kod, ali NIJE kompajliran niti testiran u ovoj
; razvojnoj sesiji jer Inno Setup i sam Windows installer zahtevaju Windows okruzenje, koje ovde nije
; dostupno (razvoj je radjen u Linux kontejneru). Pre prve zvanicne verzije, ovaj .iss fajl mora da se
; kompajlira i testira na Windows racunaru: instalacija, pokretanje, otvaranje projekta, izvoz videa,
; deinstalacija - tacno kako trazi specifikacija.
;
; Pretpostavlja da je aplikacija vec objavljena (dotnet publish) u folderu:
;   ..\publish\win-x64\
; pogledati scripts\build-release.ps1

#define MyAppName "NP Video Studio"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "NP Video Studio"
#define MyAppExeName "NPVideoStudio.exe"
#define MyProjectFileExt ".npvsproject"

[Setup]
AppId={{B9E3B6E1-6D0B-4C6C-9C8B-NPVIDEOSTUDIO1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=NPVideoStudio-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "serbianlatin"; MessagesFile: "compiler:Languages\SerbianLatin.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Napravi prečicu na Radnoj površini"; GroupDescription: "Dodatne prečice:"
Name: "associate"; Description: "Poveži .npvsproject fajlove sa NP Video Studio"; GroupDescription: "Registracija fajlova:"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Deinstaliraj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\{#MyProjectFileExt}"; ValueType: string; ValueName: ""; ValueData: "NPVideoStudioProject"; Tasks: associate; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject"; ValueType: string; ValueName: ""; ValueData: "NP Video Studio projekat"; Tasks: associate; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\NPVideoStudioProject\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Pokreni {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Namerno NE brišemo korisničke projekte, podešavanja niti logove pri deinstalaciji -
; samo fajlove same aplikacije (kopirane u [Files]). Vidi [Code] odeljak ispod za opcioni
; upit da korisnik sam izabere brisanje svojih podataka.

[Code]
function InitializeUninstall(): Boolean;
var
  AppDataDir: String;
  Response: Integer;
begin
  Result := True;
  AppDataDir := ExpandConstant('{localappdata}\NP Video Studio');
  if DirExists(AppDataDir) then
  begin
    Response := MsgBox('Da li želite da obrišete i sačuvana podešavanja, logove i keš NP Video Studio programa?' + #13#10 +
      '(Vaši projekti se NE brišu ovom opcijom - oni ostaju na disku gde god da ste ih sačuvali.)',
      mbConfirmation, MB_YESNO);
    if Response = IDYES then
      DelTree(AppDataDir, True, True, True);
  end;
end;
