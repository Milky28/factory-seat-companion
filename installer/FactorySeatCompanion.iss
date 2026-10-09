; Player installer: copies the plugin, companion CLI, ready config and overlay into an existing SimHub folder.
; Built by scripts/Package.ps1, which passes AppVersion, NumericVersion, SourceDir and OutputDir.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\package\files"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6F3C2B1E-8E7D-4F4C-9B0A-3D2E1F5A7C91}
AppName=Factory Seat Companion
AppVersion={#AppVersion}
AppPublisher=Factory Seat Companion
AppComments=Read-only check of LMU race setup against the Factory Seat round briefing.
VersionInfoVersion={#NumericVersion}
DefaultDirName={code:DefaultSimHubDir}
DirExistsWarning=no
AppendDefaultDirName=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
; Keeps our uninstaller out of SimHub's own unins000.exe.
UninstallFilesDir={app}\FactorySeatCompanion\uninstall
UninstallDisplayName=Factory Seat Companion (SimHub plugin)
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=FactorySeatCompanion-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Messages]
SelectDirLabel3=Factory Seat Companion installs into your SimHub folder, the one that contains SimHubWPF.exe.
SelectDirBrowseLabel=If this is not your SimHub folder, click Browse to pick it.
FinishedLabel=Factory Seat Companion is installed.%n%nStart SimHub and enable Factory Seat Companion when it asks about the new plugin. Then open Dash Studio, go to Overlays and start Factory Seat Companion.

[Files]
Source: "{#SourceDir}\FactorySeatCompanion.SimHub.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\FactorySeatCompanion\*"; DestDir: "{app}\FactorySeatCompanion"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\DashTemplates\FactorySeatCompanion\*"; DestDir: "{app}\DashTemplates\FactorySeatCompanion"; Flags: ignoreversion recursesubdirs

[InstallDelete]
; SimHub caches the overlay size here; a stale one keeps an old size after an update.
Type: files; Name: "{app}\DashTemplates\FactorySeatCompanion\FactorySeatCompanion.djson.metadata"

[UninstallDelete]
Type: files; Name: "{app}\DashTemplates\FactorySeatCompanion\FactorySeatCompanion.djson.metadata"
Type: dirifempty; Name: "{app}\DashTemplates\FactorySeatCompanion"
Type: dirifempty; Name: "{app}\FactorySeatCompanion"

[Code]
const
  UninstallRoot = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';

// SimHub's own uninstall entry records where it was installed.
function FindSimHubIn(RootKey: Integer; var Dir: String): Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  Name, Location: String;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey, UninstallRoot, Keys) then Exit;
  for I := 0 to GetArrayLength(Keys) - 1 do
    if RegQueryStringValue(RootKey, UninstallRoot + '\' + Keys[I], 'DisplayName', Name) and
       (Pos('simhub', Lowercase(Name)) = 1) and
       RegQueryStringValue(RootKey, UninstallRoot + '\' + Keys[I], 'InstallLocation', Location) and
       FileExists(AddBackslash(Location) + 'SimHub.Plugins.dll') then
    begin
      Dir := RemoveBackslashUnlessRoot(Location);
      Result := True;
      Exit;
    end;
end;

function DefaultSimHubDir(Param: String): String;
begin
  if not (FindSimHubIn(HKLM32, Result) or FindSimHubIn(HKLM64, Result) or FindSimHubIn(HKCU, Result)) then
    Result := ExpandConstant('{commonpf32}\SimHub');
end;

function SimHubRunning(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''SimHubWPF.exe''');
    Result := Processes.Count > 0;
  except
    // If the check itself fails, carry on; files in use still make setup ask to retry.
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if not FileExists(AddBackslash(WizardDirValue) + 'SimHub.Plugins.dll') then
    begin
      MsgBox('SimHub was not found in ' + WizardDirValue + '. Pick the folder that contains SimHubWPF.exe.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = wpReady then
  begin
    while Result and SimHubRunning() do
      Result := MsgBox('SimHub is running. Exit SimHub (tray icon, Exit) so it can load the plugin, then click Retry.',
        mbInformation, MB_RETRYCANCEL) = IDRETRY;
  end;
end;
