# WyspaFluent

Wyspa is a lightweight Windows dictation app. It runs in the system tray, records short microphone clips, sends them to Groq for transcription, and inserts the resulting text into the active app.

Wyspa is designed for people who want fast speech-to-text without a heavyweight desktop client. Transcription is handled by Groq, so the local app stays small.

## 🚨 VIBE CODE ALERT 🚨

Wyspa was written effectively entirely by Codex with some cleaver interfacing with the app to get it to work pretty well. This vibe code disclosure is basically the only thing human authored. Codex did the rest of this repo too :)

## New in v0.9.1

- **Experimental Stream Fix:** opt-in proofreading after a streaming dictation ends. Requires Stream Mode. Conservative edits preserve wording, names, numbers and meaning; broader rewrites are rejected.
- **Dictation-only correction:** supported editors can replace the exact passage Wyspa streamed with its cleaned final version. Existing field text is excluded from cleanup. Cursor changes, edits or unavailable range information leave the field alone and keep the final version on the clipboard.
- **Complete final audio pass:** brief pauses no longer finalize partial phrases. Final recognition uses the complete captured audio, and live updates prioritize accuracy over speed.
- **Honest processing status:** the blue/teal animated overlay stays visible until recognition, proofreading and delivery finish. SmartListen can capture the next utterance while earlier output finishes in order.

## Stream Mode

- **Stream Mode:** a separate opt-in switch under Settings → Audio & Capture → Capture & Shortcuts. Works with Toggle, Hold to Talk, and SmartListen.
- **Words during recording:** frequent overlapping Groq audio snapshots append words as recognition stabilizes, then flush the remaining words on Stop. The microphone keeps recording during requests.
- **Stream Mode off:** transcription and cleanup finish before a single insertion, using the existing Paste/Type setting.
- **Cumulative clipboard:** every delivered update replaces the clipboard with the whole current dictation; the next listening session starts a fresh transcript. Stream Mode inserts into the selected field and bypasses spoken actions, spoken punctuation commands, and Tone Re-write.

Groq's Whisper API transcribes completed audio uploads. Stream Mode provides near-live updates using that API; it is not a native word-token stream. It uses more requests/audio allowance and depends on your connection and Groq rate limits. See [Stream Mode behavior and limits](docs/STREAM_MODE.md).

## Introduced in v0.8

- **WyspaFluent:** a Fluent-inspired interface with clearer sections, improved contrast, consistent controls, and a GitHub About link.
- **Getting Started:** Home explains Groq account and API key setup; Conversation includes a shortcut to its settings.
- **SmartListen:** the new name for AutoCapture. The SmartListen Threshold meter lets you check microphone volume before enabling automatic capture.
- **Look & Feel:** choose Dark, Light, or System, and configure the recording overlay in one place. Preferences save automatically.
- **Automatic runtime setup:** when the required x64 .NET 10 Desktop Runtime is missing, the installer downloads and verifies the latest stable compatible version from Microsoft, installs it, and continues in the same wizard.

## Conversation and YouTube features

- **YouTube**: paste a public or unlisted single-video URL (including Shorts and completed broadcasts). Wyspa downloads its audio, splits long recordings into five-minute parts, transcribes with your saved Groq key, and saves the text. No playlists or signed-in videos.
- **Conversation**: choose Computer call or In-person. Calls label your microphone as You and the other source as Other side; choose an output device or, on supported Windows versions, a calling app and its child processes.
- **Live notes overlay**: movable, resizable, always on top, with Start, Pause/Resume, Stop, and Summarise. Messages appear as responses arrive and are ordered using speech timestamps. Your messages appear on the right.
- **Room speaker identification (experimental)**: bundled local models identify anonymous voices across short audio windows. Choose My room speaker to put your messages on the right. Adjust speaker matching and speech sensitivity in Conversation settings. Speaker labels can be wrong on short, noisy, or overlapping speech; uncertain speech is explicitly labelled.
- **Summaries**: use the existing saved Groq key and the summary model selected in Settings → Groq (default `openai/gpt-oss-20b`). Long conversations are summarised in parts and combined without silently dropping the end. Summarise while recording to capture the transcript available at that time, or after Stop for the finished session.
- **Persistent notes**: conversations appear in Saved conversations and YouTube imports in Saved videos and are written to `%AppData%\Wyspa\Notes` as JSON and readable TXT copies. Audio is temporary and removed after processing, cancellation, or failure. The dictation audio-retention setting does not control conversation or YouTube notes.

**Windows compatibility:** device audio capture works on Windows 10. Per-app audio capture requires build 20348 or newer (Windows 11 recommended); Windows 10 build 19045 does not provide this API. Wyspa explains this in Settings → Conversation and keeps device capture available. Browser capture selects the browser process tree, not one individual tab.

To try it: select your microphone in Settings → Audio & Capture and choose the conversation type and source in Settings → Conversation, then open Conversation and select Start. Open Floating overlay if wanted. Pause stops both sources while queued speech finishes; Stop finishes and saves the session. Cancel pending skips unfinished requests and marks the gaps. Closing the overlay leaves the session running; reopen it from the tray. Dictation and SmartListen are suspended for the session, including while paused, and restored afterward.

For YouTube, paste a video URL and select Transcribe video. Completed passages survive cancellation or later failures. YouTube may reject an otherwise public link because of a regional restriction or automated-download challenge; Wyspa reports the error without trying to use browser cookies or login sessions.

## Features

- Windows tray app with compact settings UI.
- Toggle, hold-to-talk, and SmartListen trigger modes.
- Configurable global hotkey, including macro keys such as `F13`-`F24`.
- Optional SmartListen media handling to mute system output or send play/pause while listening.
- Groq Whisper transcription using `whisper-large-v3-turbo`.
- Optional Groq writing cleanup with Formal, Casual, and Technical tones plus editable re-write prompts.
- Optional Groq intent model for commands such as copy, paste, Enter, Escape, task view, and related actions.
- In-app GitHub update check with a direct update download button when a newer installer is available.
- Paste or type insertion modes.
- Getting Started guidance for Groq account and API key setup.
- Audio Files section for local file selection, lossless WAV/AIFF compression, batch transcription, cancellation, and copy/save.
- Live recording overlay with voice waveform and adjustable transparency.
- Saved Dark, Light, or System theme selection.
- Start with Windows and start minimized options.
- Standard Windows installer with Apps & Features uninstall support.

## Requirements

For normal use:

- Windows 10 or later, x64.
- Microsoft .NET 10 Desktop Runtime x64; setup installs it automatically if missing.
- A Groq API key.
- A working microphone for dictation (not needed for file transcription).
- Internet access for Groq transcription.

For development:

- .NET 10 SDK.
- PowerShell.
- Python 3.11+ once at build time to prepare the pinned video tools and speaker models (`python scripts/prepare-v7-tools.py`). End users do not need Python.
- Inno Setup 6.7.3 or newer if you want to build the installer.

## Download And Install

Download the [v0.8 Windows installer](https://github.com/DracoManX69/Wyspa/releases/download/v0.8/WyspaSetup-0.8.0-win-x64.exe) from the [GitHub release](https://github.com/DracoManX69/Wyspa/releases/tag/v0.8):

```text
WyspaSetup-0.8.0-win-x64.exe
```

Run the installer and follow the wizard. The installer places Wyspa in your user profile by default, offers Start Menu and desktop shortcut options, and registers Wyspa in Windows Apps & Features.

To update Wyspa, run the newer setup EXE directly over the existing install. You do not need to uninstall first; the installer keeps your settings and saved Groq key.

The installer checks for a stable .NET 10 Desktop Runtime x64. If it is missing, setup downloads the latest compatible 10.0 servicing release from Microsoft, checks its SHA-512 and Microsoft signature, and runs the runtime installer while Wyspa Setup remains open. Allow the Windows administrator prompt for this machine-wide prerequisite; Wyspa itself installs per-user. Setup then continues automatically. An existing compatible runtime requires no download. Internet access is needed for a missing runtime; cancellation or failure offers a retry without installing Wyspa prematurely. The shared runtime is not removed when uninstalling Wyspa.

## First Run

1. Launch Wyspa.
2. Open Settings → Groq.
3. Paste your Groq API key.
4. Click Save and test key.
5. Choose your microphone in Settings → Audio & Capture.
6. Set your preferred trigger mode and hotkey.
7. Optional: in Settings → Audio & Capture, choose whether SmartListen should leave media alone, mute system output, or send play/pause while listening.
8. Optional: enable Groq writing cleanup in Settings → Experimental and choose Formal, Casual, or Technical tone.
9. Open Notepad or another text field and try a short dictation.

## Settings and model discovery

The sidebar has Home, Audio Files, Conversation, YouTube, and Settings. Settings sections are Groq, Conversation, Audio & Capture, Look & Feel, Privacy, System, and Experimental, in that order. Experimental contains Wake Voice, Tone Re-write and its model, and Spoken Actions and its model. Look & Feel includes Theme and Overlay settings. Configuration changes save automatically; API keys and shortcuts have explicit save buttons.

Settings → Groq loads the model list on first opening with a saved key. **Save and test key** also loads it, and **Refresh models** requests a fresh list without making an inference call. Speech-to-text choices are separate from the text models used for summaries, re-writing, and spoken action detection. The Refresh models button in Groq updates the selectors in both Groq and Experimental. The transcription and summary dropdowns in Groq and the re-write and spoken-action dropdowns in Experimental use the current active IDs returned by [Groq's models endpoint](https://console.groq.com/docs/models).

Groq does not document task capabilities in that endpoint's response. Wyspa therefore classifies known model families and excludes speech generation, moderation, and unrecognised families. Supporting a new model family may require a compatibility update; refreshing discovers new IDs within recognised families. A failed refresh keeps the last list and saved choices. If a saved model disappears from the list, Wyspa shows a message and keeps the setting until you choose a replacement.

Conversation and YouTube have independent selections, transcripts, summaries, errors, and saved-item lists. Existing JSON/TXT files stay in their original location and are filtered by their stored kind. Browsing or importing a video does not replace the conversation being recorded. The unused dictation History toggle and its no-op Clear History action have been removed; delete saved conversations and videos from their respective pages.

## Groq API Key

Wyspa uses your own Groq API key.

1. Go to <https://console.groq.com/keys>.
2. Sign in or create a Groq account.
3. Create an API key.
4. Copy the key.
5. Paste it into Wyspa Settings > Groq.
6. Click Save and test key.

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
artifacts\installer\WyspaSetup-0.9.1-win-x64.exe
```

## Release Files

For a GitHub release, upload the installer:

```text
artifacts\installer\WyspaSetup-0.9.1-win-x64.exe
```

Optional secondary asset:

```text
artifacts\publish\win-x64.zip
```

Do not publish local secrets such as `key.txt`, build folders such as `bin/` and `obj/`, or personal app data from `%AppData%\Wyspa`.

## Privacy

Wyspa sends microphone audio for each dictation to Groq, along with the selected model ID and optional language/prompt settings. If Groq writing cleanup is enabled, the transcript is also sent to Groq's chat completions endpoint to rewrite it in the selected tone. If command intent is enabled, the transcript is also sent to Groq's chat completions endpoint so the app can decide whether you meant to insert text or perform an action.

When you use Check for Updates, Wyspa calls the public GitHub latest-release endpoint for this repository and compares the latest release version with the installed app version.

Wyspa does not take screenshots, capture active-window contents, record keystroke history, or intentionally log transcripts. SmartListen media handling only uses Windows output mute state or the standard media play/pause key; it does not inspect what you are playing. See [docs/PRIVACY.md](docs/PRIVACY.md) for details.

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
