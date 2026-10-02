# Wyspa Privacy

Wyspa is a bring-your-own-Groq-key dictation app. It records short microphone clips, sends them to Groq for transcription, and inserts the result into the active app.

## AI Disclosure

Wyspa and this privacy document were written and produced with AI assistance from Codex. This document is descriptive, not legal advice.

## Summary

Wyspa does not include analytics or screenshot/active-window-content capture. Ordinary dictation history is not stored. Conversation and YouTube modes deliberately save transcripts and summaries locally, as described below.

Because transcription is performed by Groq, dictated audio is sent to Groq when you use the app. If Groq writing cleanup is enabled, transcript text is also sent to Groq for rewriting in the selected tone. If command intent is enabled, transcript text may also be sent to Groq for intent interpretation.

## Data Sent To Groq

For transcription, Wyspa sends:

- the microphone audio clip for dictation; audio files selected in Audio Files after Transcribe; microphone and selected output audio after starting Conversation; or audio downloaded from a YouTube URL after Transcribe video;
- the selected transcription model ID, normally `whisper-large-v3-turbo`;
- optional language setting, such as `en`;
- optional custom prompt or vocabulary text;
- temperature and response format settings required by the transcription request.

For command intent, if enabled, Wyspa sends:

- the transcript text;
- the selected intent model ID, normally `llama-3.3-70b-versatile`;
- an instruction asking the model to classify the transcript as text insertion, action, or ignore.

Command intent is used for actions such as copy, paste, cut, select all, undo, redo, Enter, Tab, Escape, Backspace, Delete, and task view.

For Groq writing cleanup, if enabled, Wyspa sends:

- the transcript text after basic local cleanup;
- the selected writing cleanup model ID, defaulting to `openai/gpt-oss-20b` for new settings;
- the selected tone, Formal, Casual, or Technical;
- the selected tone re-write prompt, which can be edited in settings.

For update checks, if you click Check for Updates, Wyspa sends a standard HTTPS request to GitHub's public latest-release API for this repository. The request includes a Wyspa user-agent with the installed version so GitHub can return the latest release metadata.

## Data Not Intentionally Sent

Wyspa does not intentionally send:

- screenshots;
- active-window contents;
- clipboard contents;
- keystroke history;
- file contents other than audio files you explicitly choose to transcribe;
- saved transcripts, except the selected note when you explicitly click Summarise;
- crash logs;
- your Groq API key, except as the authorization header required to call Groq.

The transcript is placed on the local clipboard when paste insertion is used. This happens locally so Wyspa can paste into the active app.

If AutoCapture media handling is enabled, Wyspa may locally mute the default Windows output device or send the standard Windows play/pause media key while AutoCapture listening is on. Wyspa does not inspect track names, app media libraries, or what audio is playing.

## Data Stored Locally

Wyspa stores non-secret settings in:

```text
%AppData%\Wyspa\settings.json
```

Examples include:

- selected microphone;
- hotkey;
- trigger mode;
- model IDs;
- tone re-write prompts;
- overlay opacity;
- AutoCapture media handling;
- insertion mode;
- start minimized/start with Windows settings;
- privacy-related toggles such as dictation audio retention.

The Groq API key is stored separately in:

```text
%AppData%\Wyspa\groq-key.bin
```

That file is protected with Windows DPAPI for the current Windows user. It is not plaintext JSON.

Crash logs, if created, are written to:

```text
%AppData%\Wyspa\crash.log
```

## Temporary Audio

Wyspa records temporary audio for transcription. Temporary files are created under the Windows temp folder and are intended to be deleted after transcription.

If audio retention/debugging is enabled in settings, audio may be kept for troubleshooting. Do not enable debug audio retention for sensitive dictation.

## Clipboard Use

In paste mode, Wyspa:

1. saves the current clipboard data where possible;
2. places the transcript on the clipboard;
3. sends Ctrl+V;
4. attempts to restore the previous clipboard data.

If insertion fails and fallback behavior is enabled, the transcript may remain on the clipboard so you can paste it manually.

## Start With Windows

Start with Windows uses the current user's Run registry key:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The startup command launches Wyspa with `--minimized`.

## Uninstall And Data Removal

The uninstaller asks whether to keep data.

Choose Yes to preserve:

```text
%AppData%\Wyspa
```

Choose No to remove settings, saved conversation/YouTube notes, crash logs, and the encrypted Groq API key.

You can also remove the saved Groq key from inside Wyspa settings.

## Third-Party Service

Groq processes the audio and optional transcript text sent through its API. Review Groq's own terms and privacy documentation before using Wyspa with sensitive information.

## File Transcription

Audio Files uses local lossless compression where applicable before uploading to Groq. It preserves source audio quality and never modifies or deletes source files. Selection alone does not upload audio. File names may accompany the multipart upload; full local paths are not sent.

File transcription uses the saved transcription model, language, and custom vocabulary prompt. It does not send transcripts to writing cleanup or intent models. Temporary compressed/split files are removed after success, failure, or cancellation, regardless of the microphone debug-retention setting. A crash or forced termination may leave files under `%TEMP%\Wyspa\FileTranscription`.

File transcripts are held in memory until cleared or the app exits. Copy and Save are explicit local actions. Cancellation stops future uploads but cannot recall audio already sent to Groq.


## Conversation notes and YouTube (0.7.0)

Conversation capture starts only when you select Start. Computer-call mode sends microphone and selected output audio to Groq in separate short requests. Output-device capture includes all sound on that device. App capture includes the selected process and its child processes, not a single browser tab. In-person mode sends the microphone audio to Groq; speaker segmentation and matching run locally using bundled models. A short rolling audio context and speaker embeddings exist only in memory for that session and are cleared when it finishes; they are not saved or uploaded as speaker profiles.

Summarise sends the selected transcript, speaker labels and timestamps to Groq using the existing protected API key and the chosen chat model. Long notes may require several summarisation requests. No automatic summary is requested.

YouTube import contacts YouTube and its media hosts through bundled yt-dlp, then sends the downloaded audio to Groq. It does not read browser cookies or signed-in sessions. The video URL and title are saved with the transcript.

Conversation and YouTube transcripts and summaries are automatically retained on this PC in `%AppData%\Wyspa\Notes`, with a JSON file for the app and a readable TXT file for each note. These text files are not separately encrypted. Use Delete selected to delete both copies. No extra cloud notes account or sync service is used. The dictation audio-retention switch does not disable this storage. Conversation and YouTube each show only their own saved items.

Temporary audio is placed under `%TEMP%\Wyspa` and removed after its processing request finishes, including normal cancellation and failure. A force-kill, power failure, or filesystem failure may leave temporary files; they can be removed from that folder. These new modes never intentionally retain recordings, even if dictation audio debugging is enabled.

The overlay indicates capture state; closing it does not stop the session. Pause stops both sources. Stop stops capture and finishes pending transcription; Cancel pending skips remaining requests and records gap markers. Quitting allows a short bounded finish period, then cancels remaining requests and saves gap markers.

## Stream Mode and Stream Fix

Activated Stream Mode sends overlapping microphone snapshots while recording and a complete final audio pass after recording stops. This repeats some audio and increases requests compared with ordinary dictation. Its temporary PCM spool is deleted after the session; the original recording follows the existing debug-retention preference.

When experimental Stream Fix is enabled, Wyspa also sends only the current dictation transcript to the selected text cleanup model with a fixed proofreading instruction. Existing field text and previous clipboard contents are excluded from proofreading; only Wyspa's transcript for this session is cleaned. Text outside the dictated range stays local. Wyspa reads the focused field's text, caret and document identity locally to verify safe insertion/correction, and observes input/focus changes while it owns that insertion point. It does not retain a keystroke log or send surrounding document text to Groq.

Stream Mode continually replaces the clipboard with the current dictation, then with the final transcript. It intentionally does not restore the previous clipboard. Unsupported or edited fields retain their live text and use the final clipboard for manual recovery.
