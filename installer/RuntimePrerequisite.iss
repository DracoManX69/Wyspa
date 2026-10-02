// .NET Framework and .NET/ASP.NET runtimes alone do not satisfy a WPF desktop app.
var
  RuntimeDownloadPage: TDownloadWizardPage;
  RuntimeRestartRequired: Boolean;

function IsCompatibleDesktopVersion(Version: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  if (Pos('10.0.', Version) <> 1) or (Length(Version) < 6) then exit;
  for Index := 6 to Length(Version) do
    if (Version[Index] < '0') or (Version[Index] > '9') then exit;
  Result := True;
end;

function HasDesktopRuntimeFromHost(Host: String): Boolean;
var
  Output: TExecOutput;
  ExitCode, Index, SpaceAt: Integer;
  Line, Version: String;
begin
  Result := False;
  if not FileExists(Host) then exit;
  if not ExecAndCaptureOutput(Host, '--list-runtimes', '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ExitCode, Output) then exit;
  if (ExitCode <> 0) or Output.Error then exit;
  for Index := 0 to GetArrayLength(Output.StdOut) - 1 do begin
    Line := Output.StdOut[Index];
    if Pos('Microsoft.WindowsDesktop.App ', Line) = 1 then begin
      Version := Copy(Line, Length('Microsoft.WindowsDesktop.App ') + 1, Length(Line));
      SpaceAt := Pos(' ', Version);
      if SpaceAt > 0 then Version := Copy(Version, 1, SpaceAt - 1);
      if IsCompatibleDesktopVersion(Version) then begin
        Log('Compatible x64 Desktop Runtime found: ' + Version + ' via ' + Host);
        Result := True;
        exit;
      end;
    end;
  end;
end;

function HasDesktopRuntimeFromRegistry(RootKey: Integer): Boolean;
var
  Location: String;
begin
  Result := False;
  // Read the x64 installation in BOTH registry views; Microsoft's installer uses
  // the 32-bit registry view on many x64 machines. Do not trust an x86 PATH host.
  if RegQueryStringValue(RootKey, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
    'InstallLocation', Location) then
    Result := HasDesktopRuntimeFromHost(AddBackslash(Location) + 'dotnet.exe');
end;

function HasDotNetDesktopRuntime10(): Boolean;
begin
  Result := HasDesktopRuntimeFromRegistry(HKLM32) or
    HasDesktopRuntimeFromRegistry(HKLM64) or
    HasDesktopRuntimeFromHost(ExpandConstant('{commonpf64}\dotnet\dotnet.exe')) or
    HasDesktopRuntimeFromHost(ExpandConstant('{commonpf64}\dotnet\x64\dotnet.exe'));
end;

function RunRuntimeHelper(Action: String): Boolean;
var
  Output: TExecOutput;
  ExitCode, Index: Integer;
  ErrorText: AnsiString;
begin
  Result := ExecAndCaptureOutput(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\runtime-prerequisite.ps1') + '" -Action ' + Action +
    ' -Directory "' + ExpandConstant('{tmp}') + '"', '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ExitCode, Output);
  Result := Result and (ExitCode = 0) and not Output.Error;
  if not Result then begin
    for Index := 0 to GetArrayLength(Output.StdErr) - 1 do Log(Output.StdErr[Index]);
    if LoadStringFromFile(ExpandConstant('{tmp}\desktop-runtime-error.txt'), ErrorText) then
      Log(String(ErrorText));
  end;
end;

procedure InitializeWizard();
begin
  RuntimeDownloadPage := CreateDownloadPage('Installing Microsoft .NET Desktop Runtime',
    'Wyspa needs the x64 .NET 10 Desktop Runtime. Downloading the latest compatible stable release from Microsoft.', nil);
  RuntimeDownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function RuntimeInstallFailure(ExitCode: Integer; Detected: Boolean): String;
begin
  Result := '';
  if (ExitCode = 1223) or (ExitCode = 1602) then
    Result := 'The .NET installation was cancelled. Click Install to try again and allow the Windows administrator prompt.'
  else if (ExitCode <> 0) and (ExitCode <> 3010) and (ExitCode <> 1641) then
    Result := 'Microsoft .NET installation failed (code ' + IntToStr(ExitCode) + '). Click Install to retry. See the setup log for details.'
  else if not Detected then
    Result := 'The required x64 .NET 10 Desktop Runtime is still unavailable. Restart Windows if requested, then run Wyspa Setup again.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Url, Version, RuntimePath: String;
  ExitCode: Integer;
  Detected: Boolean;
begin
  Result := '';
  if HasDotNetDesktopRuntime10() then exit;
  Log('No compatible x64 .NET Desktop Runtime detected; installing prerequisite.');
  ExtractTemporaryFile('runtime-prerequisite.ps1');
  WizardForm.StatusLabel.Caption := 'Finding the latest stable .NET 10 Desktop Runtime from Microsoft...';
  if not RunRuntimeHelper('Resolve') then begin
    Result := 'Could not retrieve the .NET Desktop Runtime download from Microsoft. Check your internet connection and click Install to retry.';
    exit;
  end;
  Url := GetIniString('Runtime', 'Url', '', ExpandConstant('{tmp}\desktop-runtime.ini'));
  Version := GetIniString('Runtime', 'Version', '', ExpandConstant('{tmp}\desktop-runtime.ini'));
  if (Url = '') or not IsCompatibleDesktopVersion(Version) then begin
    Result := 'Microsoft runtime metadata could not be read. Click Install to retry.';
    exit;
  end;
  RuntimePath := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
  DeleteFile(RuntimePath);
  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add(Url, 'windowsdesktop-runtime.exe', '');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
    except
      if RuntimeDownloadPage.AbortedByUser then
        Result := 'The .NET download was cancelled. Click Install to retry, or Cancel to leave Setup.'
      else
        Result := 'The .NET download failed. Check your internet connection and click Install to retry. ' + GetExceptionMessage;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;
  if Result <> '' then exit;
  WizardForm.StatusLabel.Caption := 'Verifying the Microsoft runtime download...';
  if not RunRuntimeHelper('Verify') then begin
    DeleteFile(RuntimePath);
    Result := 'The .NET download failed its checksum or Microsoft publisher verification. It was not run. Click Install to download it again.';
    exit;
  end;
  WizardForm.StatusLabel.Caption := 'Installing .NET Desktop Runtime ' + Version + '. Allow the Windows administrator prompt to continue...';
  // Wyspa stays per-user; only Microsoft's machine-wide prerequisite elevates.
  if not ShellExec('runas', RuntimePath, '/install /passive /norestart /log "' +
    ExpandConstant('{tmp}\desktop-runtime-install.log') + '"', '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ExitCode) then begin
    Result := RuntimeInstallFailure(ExitCode, False);
    if Result = '' then Result := 'Could not start the Microsoft .NET installer. Click Install to retry.';
    exit;
  end;
  Log('Microsoft .NET installer exit code: ' + IntToStr(ExitCode));
  RuntimeRestartRequired := (ExitCode = 3010) or (ExitCode = 1641);
  Detected := HasDotNetDesktopRuntime10();
  Result := RuntimeInstallFailure(ExitCode, Detected);
  if (Result <> '') and RuntimeRestartRequired then NeedsRestart := True;
  // On success Setup proceeds directly to installing Wyspa in this wizard.
end;

function NeedRestart(): Boolean;
begin
  Result := RuntimeRestartRequired;
end;
