#ifndef ProductVersion
  #define ProductVersion "0.1.0"
#endif
#ifndef BuildRoot
  #error BuildRoot must point to a complete Windows player build.
#endif
#ifndef ReleaseRoot
  #error ReleaseRoot must point to the installer output directory.
#endif

[Setup]
AppId={{02CA9730-42CE-4B72-B278-F1158B6B3301}
AppName=Neon Frontier
AppVersion={#ProductVersion}
AppVerName=Neon Frontier {#ProductVersion}
AppPublisher=Marwan Summakieh
VersionInfoVersion={#ProductVersion}.0
VersionInfoProductName=Neon Frontier
VersionInfoDescription=Neon Frontier Setup
VersionInfoProductVersion={#ProductVersion}
DefaultDirName={localappdata}\Programs\Neon Frontier
DefaultGroupName=Neon Frontier
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19043
OutputDir={#ReleaseRoot}
OutputBaseFilename=NeonFrontier-{#ProductVersion}-Windows-Setup
SetupIconFile=Assets\NeonFrontier.ico
UninstallDisplayIcon={app}\NeonFrontier.ico
UninstallDisplayName=Neon Frontier
WizardStyle=modern
WizardImageFile=Assets\Wizard.bmp
WizardSmallImageFile=Assets\WizardSmall.bmp
WizardImageStretch=yes
DisableWelcomePage=no
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll,*.assets,*.resS
RestartApplications=no
RestartIfNeededByRun=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
Uninstallable=yes
AllowNoIcons=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#BuildRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*DoNotShip*,*BackUpThisFolder*,*.pdb,*.mdb,*.debug,*.log,*.tmp,*.bak,*.old,*.dmp,Logs\*,logs\*"
Source: "Assets\NeonFrontier.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Neon Frontier\Neon Frontier"; Filename: "{app}\NeonFrontier.exe"; WorkingDir: "{app}"; IconFilename: "{app}\NeonFrontier.ico"; Comment: "Command your army in Neon Frontier"
Name: "{userdesktop}\Neon Frontier"; Filename: "{app}\NeonFrontier.exe"; WorkingDir: "{app}"; IconFilename: "{app}\NeonFrontier.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\NeonFrontier.exe"; WorkingDir: "{app}"; Description: "Launch Neon Frontier"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function GameIsRunning(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  { Query by executable name so a running portable copy is also protected. }
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('', 'root\CIMV2');
  Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''NeonFrontier.exe''');
  Result := Processes.Count > 0;
end;

function RequireClosedGame(const Silent: Boolean): Boolean;
begin
  Result := False;
  try
    while GameIsRunning() do
    begin
      Log('NeonFrontier.exe is running; close the game before continuing.');
      if Silent then Exit;
      if MsgBox('Please close Neon Frontier before continuing, then choose Retry. Your current match will end when you close the game.',
        mbInformation, MB_RETRYCANCEL) <> IDRETRY then Exit;
    end;
    Result := True;
  except
    Log('Could not verify whether Neon Frontier is running: ' + GetExceptionMessage);
    if not Silent then
      MsgBox('Setup could not check whether Neon Frontier is running. Close the game and try again. If this continues, check that Windows Management Instrumentation is available.', mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := RequireClosedGame(WizardSilent());
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RequireClosedGame(WizardSilent()) then
    Result := 'Close Neon Frontier before installing or updating the game.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := RequireClosedGame(UninstallSilent());
end;
