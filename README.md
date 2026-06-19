# Wyspa

Wyspa is a lightweight Windows dictation app. It runs in the system tray, records short microphone clips, sends them to Groq for transcription, and inserts the resulting text into the active app.

Wyspa is designed for people who want fast speech-to-text without a heavyweight desktop client. Transcription is handled by Groq, so the local app stays small.

## 🚨 VIBE CODE ALERT 🚨

Wyspa was written effectively entirely by Codex with some cleaver interfacing with the app to get it to work pretty well. This vibe code disclosure is basically the only thing human authored. Codex did the rest of this repo too :)

## Features

- Windows tray app with compact settings UI.
- Toggle, hold-to-talk, and AutoCapture trigger modes.
- Configurable global hotkey, including macro keys such as `F13`-`F24`.
- Groq Whisper transcription using `whisper-large-v3-turbo`.
- Optional Groq intent model for commands such as copy, paste, Enter, Escape, task view, and related actions.
- Paste or type insertion modes.
- Scratchpad for testing transcription inside the app.
- Live recording overlay with voice waveform and adjustable transparency.
- Light/dark theme support following the Windows system theme.
- Start with Windows and start minimized options.
- Standard Windows installer with Apps & Features uninstall support.

## Requirements

For normal use:

- Windows 10 or later, x64.
- Microsoft .NET 10 Desktop Runtime x64.
- A Groq API key.
- A working microphone.
- Internet access for Groq transcription.

For development:

- .NET 10 SDK.
- PowerShell.
- Inno Setup 6 if you want to build the installer.

## Download And Install

For a GitHub release, download:

```text
WyspaSetup-0.4.8-win-x64.exe
```

Run the installer and follow the wizard. The installer places Wyspa in your user profile by default, offers Start Menu and desktop shortcut options, and registers Wyspa in Windows Apps & Features.

The installer checks for the .NET 10 Desktop Runtime x64. If it is missing, setup opens the official Microsoft runtime installer or download page and asks you to run Wyspa Setup again after installing the runtime.

## First Run

1. Launch Wyspa.
2. Open the Groq tab.
3. Paste your Groq API key.
4. Click Test connection.
5. Choose your microphone in Input.
6. Set your preferred trigger mode and hotkey.
7. Open the scratchpad or another text field and try a short dictation.

## Groq API Key

Wyspa uses your own Groq API key.

1. Go to <https://console.groq.com/keys>.
2. Sign in or create a Groq account.
3. Create an API key.
4. Copy the key.
5. Paste it into Wyspa Settings > Groq.
6. Click Test connection.

The API key is stored locally with Windows user protection. It is not stored in `settings.json`.

## Build From Source

From the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

Run during development:

```powershell
dotnet run --project .\src\Wyspa.App\Wyspa.App.csproj
dotnet run --project .\src\Wyspa.App\Wyspa.App.csproj -- --minimized
```

Create the lightweight folder build:

```powershell
.\scripts\package.ps1
```

This produces:

```text
artifacts\publish\win-x64
```

The folder build keeps one visible `Wyspa.exe` at the top level and places supporting files in `Data`.

Create the Windows installer:

```powershell
.\scripts\installer.ps1
```

This produces:

```text
artifacts\installer\WyspaSetup-0.4.8-win-x64.exe
```

## Release Files

For a GitHub release, upload the installer:

```text
artifacts\installer\WyspaSetup-0.4.8-win-x64.exe
```

Optional secondary asset:

```text
artifacts\publish\win-x64.zip
```

Do not publish local secrets such as `key.txt`, build folders such as `bin/` and `obj/`, or personal app data from `%AppData%\Wyspa`.

## Privacy

Wyspa sends microphone audio for each dictation to Groq, along with the selected model ID and optional language/prompt settings. If command intent is enabled, the transcript is also sent to Groq's chat completions endpoint so the app can decide whether you meant to insert text or perform an action.

Wyspa does not take screenshots, capture active-window contents, record keystroke history, or intentionally log transcripts. See [docs/PRIVACY.md](docs/PRIVACY.md) for details.

## Uninstall

Uninstall Wyspa from Windows Settings > Apps or Control Panel. The uninstaller asks whether to keep Wyspa data.

- Choose Yes to keep `%AppData%\Wyspa`, including settings and the encrypted Groq API key.
- Choose No to remove `%AppData%\Wyspa`.

The uninstaller asks any running Wyspa tray process to quit before removing files. If the process does not exit after a short wait, it force-closes `Wyspa.exe`.

## Documentation

- [Installer](docs/INSTALLER.md)
- [Privacy](docs/PRIVACY.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

## Notes

Wyspa is original software and does not copy Wispr Flow branding, layouts, copy, icons, screenshots, or trade dress.
