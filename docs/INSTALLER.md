# Wyspa Installer

This document explains how the Wyspa Windows installer is built and what it does.

## AI Disclosure

Wyspa and this installer documentation were written and produced with AI assistance from Codex. Review release artifacts before distributing them publicly.

## Output

The installer build creates:

```text
artifacts\installer\WyspaSetup-0.4.5-win-x64.exe
```

This setup executable contains the Wyspa app files. It does not bundle the Microsoft .NET runtime.

## Build Requirements

- Windows 10 or later.
- .NET 10 SDK.
- Inno Setup 6.
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

The script first runs the Wyspa publish step, then invokes Inno Setup.

## Installer Behavior

The installer:

- installs Wyspa under the current user's profile by default;
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

## .NET Runtime Dependency

Wyspa's lightweight installer requires Microsoft .NET 10 Desktop Runtime x64 on the target machine.

During setup, the installer checks for:

```text
Microsoft.WindowsDesktop.App 10.x
```

If the runtime is missing, setup opens the official Microsoft runtime installer or download page and asks the user to run Wyspa Setup again after installing .NET.

The current installer does not silently download or install .NET inside the Wyspa wizard. To avoid a separate runtime requirement, publish a self-contained build instead, at the cost of a much larger installer.

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
- Confirm `artifacts\installer\WyspaSetup-0.4.5-win-x64.exe` exists.
- Confirm the installer opens normally on Windows.
- Install Wyspa, launch it, then uninstall it while the tray app is running.
- Confirm uninstall closes Wyspa and removes installed files.
- Confirm the keep-data and remove-data choices behave as expected.
