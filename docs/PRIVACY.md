# Wyspa Privacy

Wyspa is a bring-your-own-Groq-key dictation app. It records short microphone clips, sends them to Groq for transcription, and inserts the result into the active app.

## AI Disclosure

Wyspa and this privacy document were written and produced with AI assistance from Codex. This document is descriptive, not legal advice.

## Summary

Wyspa is designed to keep local data local where possible. It does not include analytics, screenshot capture, active-window capture, or transcript logging by default.

Because transcription is performed by Groq, dictated audio is sent to Groq when you use the app. If Groq writing cleanup is enabled, transcript text is also sent to Groq for rewriting in the selected tone. If command intent is enabled, transcript text may also be sent to Groq for intent interpretation.

## Data Sent To Groq

For transcription, Wyspa sends:

- the microphone audio clip for the current dictation;
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
- the selected writing cleanup model ID, normally `llama-3.1-8b-instant`;
- the selected tone, Formal, Casual, or Technical;
- an instruction asking the model to preserve meaning while removing filler words, false starts, and repetition.

## Data Not Intentionally Sent

Wyspa does not intentionally send:

- screenshots;
- active-window contents;
- clipboard contents;
- keystroke history;
- file contents from your computer;
- saved transcripts;
- crash logs;
- your Groq API key, except as the authorization header required to call Groq.

The transcript is placed on the local clipboard when paste insertion is used. This happens locally so Wyspa can paste into the active app.

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
- overlay opacity;
- insertion mode;
- start minimized/start with Windows settings;
- privacy-related toggles such as audio retention and history settings.

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

Choose No to remove settings, crash logs, and the encrypted Groq API key.

You can also remove the saved Groq key from inside Wyspa settings.

## Third-Party Service

Groq processes the audio and optional transcript text sent through its API. Review Groq's own terms and privacy documentation before using Wyspa with sensitive information.
