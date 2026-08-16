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
; Must be a real GUID - the old value ended in "NPVIDEOSTUDIO1", which is not hexadecimal, so it was
; never a valid GUID and Windows could not reliably match an existing install for upgrade/uninstall.
AppId={{7F3A9C41-2E5D-4B18-9A6C-D0E4F1B85C27}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Real user complaint: "NISI MI DAO MOGUĆNOST DA BIRAM U KOM FOLDERU ŽELIM DA SE INSTALIRA". Inno's
; default for this is "auto", which silently hides the destination page whenever it detects an existing
; install of the same AppId - so anyone reinstalling never got to choose. Forced on, always.
DisableDirPage=no
; Same reasoning for the very first page: with PrivilegesRequiredOverridesAllowed=dialog the wizard opens
; on an all-users/just-me question, which is fine, but the welcome page explains what is about to happen.
DisableWelcomePage=no
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
; SerbianLatin.isl is part of Inno Setup's optional Translations pack, not the base compiler install
; (the CI runner's Chocolatey "innosetup" package only ships Default.isl) - using English wizard chrome
; keeps the installer buildable everywhere. The app itself and all its content stay in Serbian.
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
