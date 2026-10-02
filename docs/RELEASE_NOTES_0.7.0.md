# Wyspa 0.7.0

Wyspa 0.7.0 adds live conversation notes and YouTube transcription, with separate pages and saved libraries. This release also brings the consolidated Settings page, task-specific Groq model dropdowns, and the updated navigation icons.

## What's new

- **Conversation:** record computer calls or in-person conversations, with timestamp-ordered live notes, Start, Pause/Resume, Stop, and summaries. A movable, resizable floating overlay keeps notes visible while you work.
- **YouTube:** transcribe public or unlisted single videos, including Shorts and completed broadcasts. Long audio is divided into parts, and completed text survives cancellation or later errors. Playlists and videos requiring sign-in are unsupported.
- **Separate saved libraries:** conversations and videos have independent selections, transcripts, summaries, status, and deletion. Existing saved files remain compatible.
- **One Settings page:** Groq, Conversation, Audio & capture, Dictation behaviour, Experimental, Privacy, and System. Conversation type and capture devices live here; redundant controls have been removed.
- **Groq model discovery:** transcription and summary model dropdowns are grouped under Groq, with a Refresh models button. Speech-to-text choices are filtered separately from text models. Failed refreshes preserve saved choices.
- **Experimental settings:** Wake Voice, Tone Re-write and its model, and Spoken Actions and its model. Overlay settings are now under System.
- **Themed navigation icons:** a talking head with voice waves for Conversation, a play button for YouTube, and an upload icon for Audio Files, with light/dark and selected-state styling.
- **Local persistence:** conversation and video transcripts and summaries are saved as JSON and readable text under `%AppData%\Wyspa\Notes`. Temporary audio is removed after processing, cancellation, or failure.

## Installation

Download **WyspaSetup-0.7.0-win-x64.exe** from this release and run it. You can install over the previous release; setup retains your existing settings, saved notes, and encrypted Groq API key.

Requires **Windows x64** and **Microsoft .NET 10 Desktop Runtime x64**. If the runtime is missing, setup directs you to Microsoft's runtime installer or download page before rerunning setup. The Wyspa installer does not bundle .NET.

Video tools, local speaker models, and the FLAC encoder are included. End users do not need Python or a separate model download. Third-party notices and FLAC source are included in the package. `SHA256SUMS-0.7.0.txt` contains the installer checksum.

## Compatibility and limitations

- Output-device audio capture supports Windows 10. Selected-app capture requires Windows build 20348 or later (Windows 11 recommended); browser selection captures its process tree, not a single tab.
- In-person speaker identification is experimental. Short, noisy, or overlapping speech can receive uncertain or incorrect labels.
- Groq's model-list response does not document task capabilities. Wyspa filters recognised model families; entirely new families may need an app update before appearing. Refresh discovers current available IDs within supported families.
- Public YouTube videos can still fail because of upstream download challenges or regional restrictions. Wyspa does not use browser cookies or signed-in sessions.

## Validation

- **108 automated tests passed, zero failures and zero skips**, including native FLAC compression and lossless splitting checks.
- Native Windows WPF checks cover all five navigation pages, settings groups, filtered model selection, refresh and persistence, separate libraries, and simulated conversation start/pause/resume/stop. No WPF binding errors.
- Normal and minimum window sizes and light/dark navigation were rendered for review.
- Windows x64 application packaging and Inno Setup compilation completed successfully.
- Installation over an existing user installation, uninstall, real calls/rooms, and selected-app capture on a newer Windows host were not automated for this release. Earlier live Groq, public-video, local speaker-model, and output-device checks are documented in `docs/V7_VALIDATION.md`.

## AI disclosure

Wyspa was written and produced with AI assistance from Codex.
