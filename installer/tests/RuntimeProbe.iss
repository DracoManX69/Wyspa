// Read-only prerequisite checks. This test executable never installs app/runtime files.
[Setup]
AppName=Wyspa prerequisite tests
AppVersion=0.8.0
DefaultDirName={tmp}\WyspaPrerequisiteTests
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
OutputDir=..\..\artifacts\v8-installer-tests
OutputBaseFilename=RuntimeProbe
ArchitecturesAllowed=x64compatible
[Files]
Source: "..\runtime-prerequisite.ps1"; Flags: dontcopy
[Code]
#include "..\RuntimePrerequisite.iss"
procedure AssertTrue(Value: Boolean; Message: String);
begin
  if not Value then RaiseException(Message);
end;
function InitializeSetup(): Boolean;
var
  Report: String;
begin
  Result := False;
  Report := ExpandConstant('{param:REPORT|{tmp}\wyspa-runtime-probe.txt}');
  try
    AssertTrue(IsCompatibleDesktopVersion('10.0.0'), 'Baseline stable runtime rejected');
    AssertTrue(IsCompatibleDesktopVersion('10.0.12'), 'Servicing runtime rejected');
    AssertTrue(not IsCompatibleDesktopVersion('10.0.1-preview'), 'Preview runtime accepted');
    AssertTrue(not IsCompatibleDesktopVersion('8.0.28'), 'Older major runtime accepted');
    AssertTrue(not IsCompatibleDesktopVersion('11.0.0'), 'Newer incompatible major runtime accepted');
    AssertTrue(not IsCompatibleDesktopVersion('10.0.'), 'Empty patch accepted');
    AssertTrue(not IsCompatibleDesktopVersion('10.0.12.1'), 'Malformed version accepted');
    AssertTrue(RuntimeInstallFailure(0, True) = '', 'Successful installation rejected');
    AssertTrue(RuntimeInstallFailure(3010, True) = '', 'Successful restart-needed installation rejected');
    AssertTrue(RuntimeInstallFailure(1641, True) = '', 'Restart-initiated code rejected');
    AssertTrue(RuntimeInstallFailure(0, False) <> '', 'Missing runtime after success ignored');
    AssertTrue(RuntimeInstallFailure(3010, False) <> '', 'Missing runtime before reboot ignored');
    AssertTrue(Pos('cancelled', RuntimeInstallFailure(1223, False)) > 0, 'UAC cancellation not explained');
    AssertTrue(Pos('cancelled', RuntimeInstallFailure(1602, False)) > 0, 'Installer cancellation not explained');
    AssertTrue(Pos('1603', RuntimeInstallFailure(1603, False)) > 0, 'Install failure code omitted');
    AssertTrue(not HasDesktopRuntimeFromHost(ExpandConstant('{tmp}\missing-dotnet.exe')), 'Missing host accepted');
    AssertTrue(HasDotNetDesktopRuntime10(), 'Installed x64 runtime not detected on the test host');
    SaveStringToFile(Report, 'PASS: version, result-code and installed-runtime detection checks.'#13#10, False);
  except
    SaveStringToFile(Report, 'FAIL: ' + GetExceptionMessage + #13#10, False);
  end;
end;
