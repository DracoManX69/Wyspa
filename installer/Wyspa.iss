#define AppName "Wyspa"
#define AppVersion "0.9.4"
#define AppVersionInfo "0.9.4.0"
#define AppPublisher "Wyspa"
#define AppExeName "Wyspa.exe"
#ifndef PublishDir
#define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{3E14E2A8-1A83-4D7E-A5F0-A4A67A1B4A7D}
AppName=WyspaFluent
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=no
DisableProgramGroupPage=no
OutputDir=..\artifacts\installer
OutputBaseFilename=WyspaSetup-{#AppVersion}-win-x64
SetupIconFile=..\src\Wyspa.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName=WyspaFluent
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
VersionInfoVersion={#AppVersionInfo}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "runtime-prerequisite.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: startmenu
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
#include "RuntimePrerequisite.iss"

var
  KeepWyspaData: Boolean;

function IsWyspaProcessRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(
      ExpandConstant('{cmd}'),
      '/C tasklist /FI "IMAGENAME eq {#AppExeName}" 2>nul | findstr /I "{#AppExeName}" >nul',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and (ResultCode = 0);
end;

procedure StopWyspaProcesses();
var
  Index: Integer;
  ResultCode: Integer;
  WyspaPath: String;
begin
  WyspaPath := ExpandConstant('{app}\{#AppExeName}');

  if FileExists(WyspaPath) then
  begin
    Exec(WyspaPath, '--quit-existing', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  for Index := 1 to 20 do
  begin
    if not IsWyspaProcessRunning() then
    begin
      exit;
    end;

    Sleep(500);
  end;

  Exec(
    ExpandConstant('{cmd}'),
    '/C taskkill /IM "{#AppExeName}" /T /F >nul 2>nul',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  KeepWyspaData := True;
  StopWyspaProcesses();

  if UninstallSilent() then
  begin
    exit;
  end;

  KeepWyspaData :=
    MsgBox(
      'Keep your Wyspa data?'#13#13 +
      'Yes keeps settings, crash logs, and the encrypted Groq API key in %AppData%\Wyspa.'#13#13 +
      'No removes Wyspa app data completely.',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON1) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
  begin
    exit;
  end;

  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');

  if not KeepWyspaData then
  begin
    DelTree(ExpandConstant('{userappdata}\{#AppName}'), True, True, True);
    DelTree(ExpandConstant('{tmp}\{#AppName}'), True, True, True);
  end;
end;
