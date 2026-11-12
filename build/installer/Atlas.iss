; The Windows installer of Atlas, built with Inno Setup 6.7 by build/publish.ps1, once per architecture.
;
; It installs a published copy of Atlas, adds it to the Start menu and to the list of installed applications,
; and updates an existing installation in place. It never touches the data of Atlas, which lives in each user's
; %LOCALAPPDATA%\ELPO\Atlas: updating keeps it, and uninstalling leaves it for the user to delete.

#ifndef AppVersion
  #error Define AppVersion, for example /DAppVersion=0.2.0-beta.1
#endif
#ifndef VersionMajor
  #error Define VersionMajor, VersionMinor and VersionPatch, the numeric parts of AppVersion
#endif
#ifndef Arch
  #error Define Arch as x64 or arm64
#endif
#ifndef SourceDir
  #error Define SourceDir, the published application with its license files
#endif
#ifndef OutputDir
  #error Define OutputDir
#endif
#ifndef OutputName
  #error Define OutputName, the installer file name without extension
#endif

; One identity for both architectures: Windows holds a single Atlas installation at a time.
#define AppId "D1538AA5-DB1C-4AEB-97A7-68AC1603F0A9"
#define AppName "ELPO Atlas"
#define AppPublisher "ELPO software"
#define AppExe "Atlas.exe"
; Names every running Atlas holds (SingleInstanceGuard.RunningName, and DefaultName for earlier versions).
; Setup and Uninstall wait until they are free.
#define AppMutexName "Local\ELPO.Atlas.Running,Local\ELPO.Atlas.SingleInstance"
; Lets Windows attribute the notifications of Atlas to its Start menu entry (NativeNotifications.AppId).
#define AppUserModelId "ELPO.Atlas"

#if Arch == "x64"
  #define AllowedArchitectures "x64os"
  #define OtherPackage "Atlas-Setup-ARM64.exe"
  #define OtherPackageMachine "un PC Windows à processeur ARM"
#elif Arch == "arm64"
  #define AllowedArchitectures "arm64"
  #define OtherPackage "Atlas-Setup-x64.exe"
  #define OtherPackageMachine "un PC à processeur Intel ou AMD"
#else
  #error Arch must be x64 or arm64
#endif

[Setup]
AppId={{{#AppId}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/ELPO-software
AppSupportURL=https://github.com/ELPO-software/Atlas/issues
AppUpdatesURL=https://github.com/ELPO-software/Atlas/releases
AppCopyright=Copyright (c) 2026 {#AppPublisher}
VersionInfoVersion={#VersionMajor}.{#VersionMinor}.{#VersionPatch}.0
VersionInfoTextVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription=Installation de {#AppName}
VersionInfoCompany={#AppPublisher}

; Program Files for all users, or the user's own programs folder when installed for one account only.
DefaultDirName={autopf}\ELPO\Atlas
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
UsePreviousPrivileges=yes
ArchitecturesAllowed={#AllowedArchitectures}
ArchitecturesInstallIn64BitMode={#AllowedArchitectures}
MinVersion=10.0.17763

; Setup and Uninstall refuse to work while Atlas or another Atlas setup runs.
AppMutex={#AppMutexName}
SetupMutex=ELPO.Atlas.Setup,Global\ELPO.Atlas.Setup
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\..\src\Atlas.App\Assets\atlas.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern dynamic windows11 hidebevels
ShowLanguageDialog=no
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
OnlyOnTheseArchitectures=Ce programme ne peut être installé que sur des versions de Windows qui prennent en charge ces architectures : %n%n%1%n%nPour {#OtherPackageMachine}, installez {#OtherPackage}.

[CustomMessages]
french.InstalledForAllUsers={#AppName} est déjà installé pour tous les utilisateurs de cet ordinateur. Pour le mettre à jour, relancez l'installation et choisissez « Installer pour tous les utilisateurs ».
french.InstalledForCurrentUser={#AppName} est déjà installé pour votre compte seulement. Pour le mettre à jour, relancez l'installation et choisissez « Installer seulement pour moi ».
french.NewerInstalled=Une version plus récente de {#AppName} est déjà installée (%1).%n%nCette version plus ancienne pourrait ne pas savoir ouvrir vos données. Voulez-vous quand même l'installer ?
french.DataKept={#AppName} a été désinstallé.%n%nVos espaces de travail et vos réglages sont conservés dans le dossier %1 de chaque compte qui a utilisé Atlas. Supprimez ce dossier pour les effacer.
french.AppComment=Enregistrez et restaurez vos environnements de travail

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppComment}"; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppComment}"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Run]
; A post-install entry runs with the rights of the user who started Setup, not elevated.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppId}}_is1';

function CurrentModeRoot(): Integer;
begin
  if IsAdminInstallMode then
    Result := HKEY_LOCAL_MACHINE_64
  else
    Result := HKEY_CURRENT_USER;
end;

function OtherModeRoot(): Integer;
begin
  if IsAdminInstallMode then
    Result := HKEY_CURRENT_USER
  else
    Result := HKEY_LOCAL_MACHINE_64;
end;

{ Two installations, one for all users and one for a single account, would compete for the Start menu and for updates. }
function InstalledInTheOtherMode(): Boolean;
begin
  Result := RegKeyExists(OtherModeRoot(), UninstallKey);
  if Result then
  begin
    if IsAdminInstallMode then
      SuppressibleMsgBox(CustomMessage('InstalledForCurrentUser'), mbError, MB_OK, IDOK)
    else
      SuppressibleMsgBox(CustomMessage('InstalledForAllUsers'), mbError, MB_OK, IDOK);
  end;
end;

{ Installing an older version over a newer one is possible, but only on purpose. }
function DowngradeRefused(): Boolean;
var
  Location, InstalledText: String;
  Installed, Own: Int64;
begin
  Result := False;
  if not RegQueryStringValue(CurrentModeRoot(), UninstallKey, 'InstallLocation', Location) then
    Exit;
  if not GetPackedVersion(AddBackslash(Location) + '{#AppExe}', Installed) then
    Exit;

  Own := PackVersionComponents({#VersionMajor}, {#VersionMinor}, {#VersionPatch}, 0);
  if ComparePackedVersion(Installed, Own) <= 0 then
    Exit;

  if not RegQueryStringValue(CurrentModeRoot(), UninstallKey, 'DisplayVersion', InstalledText) then
    InstalledText := '?';

  Result := SuppressibleMsgBox(FmtMessage(CustomMessage('NewerInstalled'), [InstalledText]),
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES;
end;

function InitializeSetup(): Boolean;
begin
  Result := not InstalledInTheOtherMode() and not DowngradeRefused();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(FmtMessage(CustomMessage('DataKept'), ['%LOCALAPPDATA%\ELPO\Atlas']), mbInformation, MB_OK);
end;
