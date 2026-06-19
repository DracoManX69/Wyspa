#define AppName "Wyspa"
#define AppVersion "0.4.5"
#define AppVersionInfo "0.4.5.0"
#define AppPublisher "Wyspa"
#define AppExeName "Wyspa.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{3E14E2A8-1A83-4D7E-A5F0-A4A67A1B4A7D}
AppName={#AppName}
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
UninstallDisplayName={#AppName}
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
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Data\*"; DestDir: "{app}\Data"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: startmenu
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNetDesktopRuntimePage = 'https://dotnet.microsoft.com/download/dotnet/10.0';

var
  KeepWyspaData: Boolean;

function HasDotNetDesktopRuntime10FromCommand(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(
      ExpandConstant('{cmd}'),
      '/C dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 10." >nul',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and (ResultCode = 0);
end;

function HasDotNetDesktopRuntime10FromRegistryRoot(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Result := False;

  if RegQueryStringValue(
    RootKey,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    'Version',
    Version) then
  begin
    Result := Pos('10.', Version) = 1;
  end;
end;

function HasDotNetDesktopRuntime10(): Boolean;
begin
  Result :=
    HasDotNetDesktopRuntime10FromCommand() or
    HasDotNetDesktopRuntime10FromRegistryRoot(HKLM64) or
    HasDotNetDesktopRuntime10FromRegistryRoot(HKCU64);
end;

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

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if HasDotNetDesktopRuntime10() then
  begin
    exit;
  end;

  if WizardSilent() then
  begin
    MsgBox(
      'Wyspa requires the Microsoft .NET 10 Desktop Runtime for Windows x64. Install it from ' +
      DotNetDesktopRuntimePage + ', then run Wyspa Setup again.',
      mbCriticalError,
      MB_OK);
    Result := False;
    exit;
  end;

  if MsgBox(
    'Wyspa requires the Microsoft .NET 10 Desktop Runtime for Windows x64.'#13#13 +
    'Choose Yes to open the official Microsoft runtime installer. After it finishes, run Wyspa Setup again.'#13#13 +
    'Choose No to open the .NET 10 download page instead.',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON1) = IDYES then
  begin
    ShellExec('open', DotNetDesktopRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end
  else
  begin
    ShellExec('open', DotNetDesktopRuntimePage, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;

  MsgBox(
    'Install the .NET 10 Desktop Runtime, then run Wyspa Setup again.',
    mbInformation,
    MB_OK);
  Result := False;
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
