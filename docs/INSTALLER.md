# Wyspa Installer

This document explains how the Wyspa Windows installer is built and what it does.

## AI Disclosure

Wyspa and this installer documentation were written and produced with AI assistance from Codex. Review release artifacts before distributing them publicly.

## Output

The installer build creates:

```text
artifacts\installer\WyspaSetup-0.8.0-win-x64.exe
```

This setup executable contains the Wyspa app files. It does not bundle the Microsoft .NET runtime.

## Build Requirements

- Windows 10 or later.
- .NET 10 SDK.
- Inno Setup 6.7.3 or newer.
- PowerShell.

Install Inno Setup 6 with winget:

```powershell
winget install --id JRSoftware.InnoSetup -e -s winget -i
```

The build script detects common Inno Setup locations, including the winget per-user path:

```text
C:\Users\<you>\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```

## Build Command

From the repository root:

```powershell
.\scripts\installer.ps1
```

If the compiler is not detected automatically:

```powershell
.\scripts\installer.ps1 -InnoSetupCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Before the first build, run `python scripts/prepare-v7-tools.py` (Python 3.11+). This fetches checksum-verified video utilities and local speaker models; the installed app never installs tools itself. Then the installer script runs the Wyspa publish step and invokes Inno Setup.

A self-contained portable build can also be produced with:

```powershell
dotnet publish src/Wyspa.App/Wyspa.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/portable/0.8.0
```

Keep the entire portable folder together, including `Tools` and native DLLs. This version includes its .NET runtime. The standard installer remains framework-dependent and installs the compatible runtime automatically if it is missing.

## Installer Behavior

The installer:

- installs Wyspa under the current user's profile by default;
- upgrades an existing Wyspa install when a newer setup EXE is run over the top;
- lets the user choose the install location;
- offers Start Menu shortcut creation;
- offers optional desktop shortcut creation;
- optionally launches Wyspa after installation;
- registers Wyspa in Windows Apps & Features and Control Panel;
- uses the Wyspa application icon for setup and uninstall entries.

The default install path is:

```text
%LocalAppData%\Programs\Wyspa
```

## Update Behavior

Wyspa uses a stable installer application ID and install directory. To update, run the newer `WyspaSetup-*-win-x64.exe` directly. You do not need to uninstall first.

During an update, setup asks the running tray app to quit, replaces the installed program files, keeps `%AppData%\Wyspa`, and leaves the saved settings and encrypted Groq API key in place.

## .NET Runtime Dependency

Wyspa's lightweight installer requires Microsoft .NET 10 Desktop Runtime x64 on the target machine.

Setup uses the registered x64 .NET host (both registry views) and standard x64 locations to check `dotnet --list-runtimes` for a stable `Microsoft.WindowsDesktop.App 10.0.*`. An x86 host on PATH, .NET Framework, the base runtime alone, previews, and another major version do not satisfy this check.

When missing, Setup:

1. Resolves the latest stable 10.0 Desktop Runtime x64 through Microsoft's HTTPS release metadata. The compatible major stays pinned to the app's target framework while the servicing version is resolved at installation time.
2. Downloads the installer inside the wizard with progress and cancellation support.
3. Verifies the published SHA-512, valid Microsoft Authenticode signature, and matching x64 Desktop Runtime product/version.
4. Runs Microsoft's installer with `/install /passive /norestart`, requesting administrator approval for this prerequisite only.
5. Rechecks the runtime and continues installing Wyspa without requiring a second setup run on normal success.

Download, verification, elevation cancellation, and runtime installation errors leave setup at a retryable error before Wyspa files are installed. Return codes 3010 and 1641 are handled as restart outcomes. Setup does not request an immediate restart from Microsoft's installer; if the runtime remains unavailable until reboot, it explains that a restart and rerun are required. The shared runtime is not removed by the Wyspa uninstaller.

Internet access and Windows PowerShell are required only when the runtime is missing. An existing compatible runtime skips all prerequisite network activity. Microsoft documents the Desktop Runtime and installer options in [Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

The implementation is shared with a read-only Inno test harness in `installer/tests/RuntimeProbe.iss`. See [v0.8 validation](V8_VALIDATION.md) for evidence and outstanding clean-machine checks.

## Installed Files

The normal install contains:

```text
Wyspa.exe
Data\Wyspa.dll
Data\Wyspa.Core.dll
Data\Wyspa.Infrastructure.dll
Data\Wyspa.deps.json
Data\Wyspa.runtimeconfig.json
Data\NAudio.Core.dll
Data\NAudio.WinMM.dll
```

Debug symbols, local keys, and development-only files should not be included.

## Uninstall Behavior

Wyspa can be removed through Windows Settings > Apps or Control Panel.

Before file removal, the uninstaller:

1. Starts `Wyspa.exe --quit-existing` to ask the tray app to exit.
2. Waits briefly for `Wyspa.exe` to stop.
3. Force-closes `Wyspa.exe` if it is still running.
4. Removes installed program files.
5. Removes the Wyspa Start with Windows registry value.

## Keep Data Prompt

During uninstall, the user is asked whether to keep Wyspa data.

Choosing Yes preserves:

```text
%AppData%\Wyspa
```

That folder may include:

- `settings.json`
- `crash.log`
- `groq-key.bin`

Choosing No removes `%AppData%\Wyspa`, including the encrypted Groq API key.

## Release Checklist

Before publishing a GitHub release:

- Run `.\scripts\test.ps1`.
- Run `.\scripts\installer.ps1`.
- Confirm `artifacts\installer\WyspaSetup-0.8.0-win-x64.exe` exists.
- Confirm the installer opens normally on Windows.
- Install Wyspa, launch it, then uninstall it while the tray app is running.
- Confirm uninstall closes Wyspa and removes installed files.
- Confirm the keep-data and remove-data choices behave as expected.

The `Data\Tools\Flac` folder contains the offline FLAC encoder, its license texts, and corresponding source archive. It is included automatically in builds and installers.
