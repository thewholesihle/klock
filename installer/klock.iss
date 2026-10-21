; Klock installer (Inno Setup 6.3+). Built by build.ps1; run directly with:
;   ISCC.exe /DAppVersion=0.1.0 /DArch=x64 /DStageDir=..\dist\x64\stage /DOutDir=..\dist installer\klock.iss
;
; What it does
;   * Copies Klock.scr to System32 (Windows only lists screensavers from there) and the settings app to Program Files.
;   * Records InstallDir so "Settings..." in the Screen Saver dialog can find the settings app.
;   * Removes any previous version first, so an upgrade is always a clean install.
;   * Opens Windows' Screen Saver Settings with Klock pre-selected (it never changes your screensaver silently).
;   * Uninstall removes every file and key it created; if Klock was the active screensaver, that is switched off.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef StageDir
  #define StageDir "..\dist\x64\stage"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

#define AppName "Klock"
#define AppPublisher "Klock"
#define ScrName "Klock.scr"
#define SettingsExe "Klock.Settings.exe"

[Setup]
; Keep this GUID forever: it is how upgrades find the previous install.
AppId={{B6C1F1A0-7E52-4B5E-9C0E-4B1E5C0A6B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://example.com
AppSupportURL=https://example.com/support
AppUpdatesURL=https://example.com/klock
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Writing to System32 needs administrator rights.
PrivilegesRequired=admin
MinVersion=10.0.17763
OutputDir={#OutDir}
OutputBaseFilename=Klock-Setup-{#AppVersion}-{#Arch}
SetupIconFile={#StageDir}\klock.ico
UninstallDisplayIcon={app}\klock.ico
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto
UsePreviousLanguage=no
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.scr
RestartApplications=no
; Signing placeholder: build.ps1 signs the finished Setup.exe with signtool when -Sign is passed.
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
#define LangDir AddBackslash(CompilerPath) + "Languages\"
#if FileExists(LangDir + "French.isl")
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
#endif
#if FileExists(LangDir + "German.isl")
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
#endif
#if FileExists(LangDir + "Spanish.isl")
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
#endif
#if FileExists(LangDir + "Italian.isl")
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
#endif
#if FileExists(LangDir + "Japanese.isl")
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"
#endif
#if FileExists(LangDir + "Russian.isl")
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
#endif
#if FileExists(LangDir + "Turkish.isl")
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
#endif

[CustomMessages]
en.OpenScreenSaverSettings=Choose Klock as my screen saver
fr.OpenScreenSaverSettings=Choisir Klock comme écran de veille
de.OpenScreenSaverSettings=Klock als Bildschirmschoner auswählen
es.OpenScreenSaverSettings=Elegir Klock como protector de pantalla
it.OpenScreenSaverSettings=Scegli Klock come screen saver
ja.OpenScreenSaverSettings=Klock をスクリーンセーバーに設定する
ru.OpenScreenSaverSettings=Выбрать Klock в качестве заставки
tr.OpenScreenSaverSettings=Klock'u ekran koruyucum olarak seç
en.RemoveSettingsPrompt=Also remove your Klock settings?
fr.RemoveSettingsPrompt=Supprimer également vos réglages Klock ?
de.RemoveSettingsPrompt=Auch Ihre Klock-Einstellungen entfernen?
es.RemoveSettingsPrompt=¿Quitar también tus ajustes de Klock?
it.RemoveSettingsPrompt=Rimuovere anche le impostazioni di Klock?
ja.RemoveSettingsPrompt=Klock の設定も削除しますか？
ru.RemoveSettingsPrompt=Удалить также настройки Klock?
tr.RemoveSettingsPrompt=Klock ayarlarınız da kaldırılsın mı?

[Files]
; System32 is the 64-bit one thanks to ArchitecturesInstallIn64BitMode. restartreplace handles a copy that is in use.
Source: "{#StageDir}\{#ScrName}"; DestDir: "{sys}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "{#StageDir}\{#SettingsExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\klock.ico"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Anything an older, multi-file layout may have left behind.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"
Type: filesandordirs; Name: "{app}\runtimes"

[Registry]
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[Icons]
Name: "{autoprograms}\{#AppName} Settings"; Filename: "{app}\{#SettingsExe}"; IconFilename: "{app}\klock.ico"

[Run]
; Opens Windows' Screen Saver Settings with Klock selected. Runs as the user who started Setup so it edits their settings.
Filename: "{sys}\rundll32.exe"; Parameters: "desk.cpl,InstallScreenSaver {sys}\{#ScrName}"; \
    Description: "{cm:OpenScreenSaverSettings}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  DesktopKey = 'Control Panel\Desktop';

function SettingsKey(): String;
begin
  Result := 'Software\{#AppName}';
end;

{ If Klock is the active screen saver, switch it off so Windows does not complain about a missing .scr. }
procedure ReleaseActiveScreenSaver();
var
  Current: String;
begin
  if RegQueryStringValue(HKCU, DesktopKey, 'SCRNSAVE.EXE', Current) then
  begin
    if SameText(ExtractFileName(Current), '{#ScrName}') then
    begin
      RegDeleteValue(HKCU, DesktopKey, 'SCRNSAVE.EXE');
      RegWriteStringValue(HKCU, DesktopKey, 'ScreenSaveActive', '0');
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    ReleaseActiveScreenSaver();

  if CurUninstallStep = usPostUninstall then
  begin
    { Settings are the user's, not ours: ask before deleting them (never during a silent uninstall). }
    if (not UninstallSilent) and (MsgBox(CustomMessage('RemoveSettingsPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      RegDeleteKeyIncludingSubkeys(HKCU, SettingsKey());
      DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);
    end;
  end;
end;
