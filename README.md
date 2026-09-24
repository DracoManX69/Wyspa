# Wyspa

Wyspa is a lightweight Windows dictation app. It runs in the system tray, records short microphone clips, sends them to Groq for transcription, and inserts the resulting text into the active app.

Wyspa is designed for people who want fast speech-to-text without a heavyweight desktop client. Transcription is handled by Groq, so the local app stays small.

## 🚨 VIBE CODE ALERT 🚨

Wyspa was written effectively entirely by Codex with some cleaver interfacing with the app to get it to work pretty well. This vibe code disclosure is basically the only thing human authored. Codex did the rest of this repo too :)

## Features

- Windows tray app with compact settings UI.
- Toggle, hold-to-talk, and AutoCapture trigger modes.
- Configurable global hotkey, including macro keys such as `F13`-`F24`.
- Optional AutoCapture media handling to mute system output or send play/pause while listening.
- Groq Whisper transcription using `whisper-large-v3-turbo`.
- Optional Groq writing cleanup with Formal, Casual, and Technical tones plus editable re-write prompts.
- Optional Groq intent model for commands such as copy, paste, Enter, Escape, task view, and related actions.
- In-app GitHub update check with a direct update download button when a newer installer is available.
- Paste or type insertion modes.
- Scratchpad for testing transcription inside the app.
- Audio Files section for local file selection, lossless WAV/AIFF compression, batch transcription, cancellation, and copy/save.
- Live recording overlay with voice waveform and adjustable transparency.
- Light/dark theme support following the Windows system theme.
- Start with Windows and start minimized options.
- Standard Windows installer with Apps & Features uninstall support.

## Requirements

For normal use:

- Windows 10 or later, x64.
- Microsoft .NET 10 Desktop Runtime x64.
- A Groq API key.
- A working microphone for dictation (not needed for file transcription).
- Internet access for Groq transcription.

For development:

- .NET 10 SDK.
- PowerShell.
- Inno Setup 6 if you want to build the installer.

## Download And Install

For a GitHub release, download:

```text
WyspaSetup-0.6.1-win-x64.exe
```

Run the installer and follow the wizard. The installer places Wyspa in your user profile by default, offers Start Menu and desktop shortcut options, and registers Wyspa in Windows Apps & Features.

To update Wyspa, run the newer setup EXE directly over the existing install. You do not need to uninstall first; the installer keeps your settings and saved Groq key.

The installer checks for the .NET 10 Desktop Runtime x64. If it is missing, setup opens the official Microsoft runtime installer or download page and asks you to run Wyspa Setup again after installing the runtime.

## First Run

1. Launch Wyspa.
2. Open the Groq tab.
3. Paste your Groq API key.
4. Click Test connection.
5. Choose your microphone in Input.
6. Set your preferred trigger mode and hotkey.
7. Optional: in Input, choose whether AutoCapture should leave media alone, mute system output, or send play/pause while listening.
8. Optional: enable Groq writing cleanup in Behavior and choose Formal, Casual, or Technical tone.
9. Open the scratchpad or another text field and try a short dictation.

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
artifacts\installer\WyspaSetup-0.6.1-win-x64.exe
```

## Release Files

For a GitHub release, upload the installer:

```text
artifacts\installer\WyspaSetup-0.6.1-win-x64.exe
```

Optional secondary asset:

```text
artifacts\publish\win-x64.zip
```

Do not publish local secrets such as `key.txt`, build folders such as `bin/` and `obj/`, or personal app data from `%AppData%\Wyspa`.

## Privacy

Wyspa sends microphone audio for each dictation to Groq, along with the selected model ID and optional language/prompt settings. If Groq writing cleanup is enabled, the transcript is also sent to Groq's chat completions endpoint to rewrite it in the selected tone. If command intent is enabled, the transcript is also sent to Groq's chat completions endpoint so the app can decide whether you meant to insert text or perform an action.

When you use Check for Updates, Wyspa calls the public GitHub latest-release endpoint for this repository and compares the latest release version with the installed app version.

Wyspa does not take screenshots, capture active-window contents, record keystroke history, or intentionally log transcripts. AutoCapture media handling only uses Windows output mute state or the standard media play/pause key; it does not inspect what you are playing. See [docs/PRIVACY.md](docs/PRIVACY.md) for details.

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

## Audio Files (0.6.1)

1. Save/test your API key in **Groq** as usual.
2. Open **Audio Files** and click **Choose files…**. Select one or more recordings from your PC.
3. Click **Transcribe**. Selection alone never uploads anything.
4. Watch preparation/transcription progress and the original versus uploaded size for each file.
5. Select a completed file and use **Copy transcript** or **Save text…**.

This uses the same Groq transcription model, language, and vocabulary prompt as dictation. It requests plain text and does not call writing cleanup or command interpretation models, even when those options are enabled for microphone dictation. Files run sequentially; retrying a batch skips completed files. Cancelling keeps completed and partial transcripts available, and stops remaining uploads. Retrying a partial file transcribes that file from the beginning.

Integer PCM WAV and AIFF are encoded locally as FLAC with verification: decoded samples, sample rate, bit depth, and channels are preserved. There is no downsampling, stereo-to-mono mixing, silence removal, or lossy re-encoding. If the original WAV is smaller, Wyspa uploads the original. Floating-point or other WAV formats that cannot be encoded losslessly are uploaded unchanged if they fit the limit. Corrupt/unsupported audio may still be rejected by Groq.

FLAC, MP3, M4A, OGG, WebM, and MPGA are already compressed and normally uploaded unchanged. PCM/FLAC audio above the 24 MB per-upload safety limit is split into consecutive lossless parts and its transcripts are joined in order. Splitting preserves samples, but transcription at a part boundary can be less accurate because the model sees each part independently. Other compressed files above 24 MB need to be split locally first. Multi-track containers use the provider's first audio track only.

Temporary lossless files are deleted on success, failure, or cancellation. Source files are never changed or deleted. Transcripts stay in memory until cleared or Wyspa exits unless you explicitly save/copy them. A forced process termination or power loss can leave temporary files under `%TEMP%\Wyspa\FileTranscription`.

Lossless compression reduces upload bandwidth, but it does not shorten the recording or reduce duration-based transcription billing. See [Groq speech-to-text documentation](https://console.groq.com/docs/speech-to-text) for provider limits and billing rules.

### Lossless integration tests

Windows tests use the bundled FLAC 1.5.0 tool. On Linux/macOS, set `WYSPA_FLAC_PATH` to a native FLAC executable to enable the codec integration tests:

```bash
WYSPA_FLAC_PATH=/usr/bin/flac dotnet test Wyspa.slnx --configuration Release
```

The codec tests verify byte-identical PCM after compression and after splitting a file larger than the upload limit. Queue/network tests use fakes and require no API key.
