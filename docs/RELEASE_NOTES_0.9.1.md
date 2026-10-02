# Wyspa 0.9.1 — Stream Mode and Stream Fix

Wyspa 0.9.1 adds near-live dictation, a cumulative clipboard, and optional proofreading of the current dictation when recording ends.

## Stream Mode

- Enable **Settings → Audio & Capture → Capture & Shortcuts → Stream Mode**. This separate switch works with Toggle, Hold to Talk and SmartListen.
- Words appear in the selected field as recognition stabilizes. Each clipboard update contains the complete dictation for the current recording.
- When Stream Mode is off, Wyspa finishes transcription and the usual cleanup before inserting the result once.
- Groq receives overlapping audio snapshots while recording, followed by a complete final audio pass. Pauses and the final microphone callback are retained to improve sentence endings.
- The animated blue/teal **Transcribing** overlay stays visible while recognition, proofreading or text delivery is still processing. SmartListen can begin capturing a new utterance while an earlier recording finishes, with output delivered in order.

## Experimental Stream Fix

Enable **Settings → Experimental → Stream Fix** while Stream Mode is on.

- Checks punctuation, hesitations, repeated function words and simple grammar using conservative proofreading rules.
- Cleans only the current dictated transcript. Existing field text and previous clipboard contents are excluded from the model's input.
- Replaces only the verified passage Wyspa inserted during that recording, using the selected Paste or Type method. Supported editors include the Word, native WPF and Edge controls covered by testing.
- Moving the cursor, editing, changing fields or unavailable text-range information causes clipboard fallback. Review the final clipboard before pasting to avoid duplicating text already delivered.
- Uses the selected Tone Re-write model with its own proofreading rules; Tone Re-write does not need to be enabled. New settings default to `openai/gpt-oss-20b`, while existing model selections are preserved.

Stream Fix remains experimental. Editor compatibility varies, and broader rewrites are deliberately rejected. Windows selection and input delivery are separate operations, so concurrent document changes can still affect insertion. Near-live recognition uses more Groq requests and audio allowance than ordinary dictation.

## Installation

Download **WyspaSetup-0.9.1-win-x64.exe** and run it on Windows x64. The installer checks for the .NET 10 Desktop Runtime and offers the existing prerequisite installation flow when needed. Stream Mode and Stream Fix are opt-in settings.

## Validation

- **155 automated tests passed; zero failed or skipped**, with native FLAC coverage enabled.
- Real Windows checks passed in Microsoft Word, WPF text boxes, Microsoft Edge textareas and a basic browser contenteditable field.
- Checks covered surrounding-text preservation, repeated words elsewhere, previous dictation sessions, Paste/Type delivery, Unicode, existing selections, cursor movement, edits and clipboard fallback.
- Settings persistence, Stream Fix's dependency on Stream Mode, and the processing overlay passed native WPF checks.
- The installer contains the same application binaries exercised by the native tests.

Physical microphone accuracy, long real-world dictation, all editor variants and installer install/upgrade/uninstall lifecycle checks remain for hands-on acceptance. See [the validation record](https://github.com/DracoManX69/Wyspa/blob/v0.9.1/docs/V091_VALIDATION.md) for scope and evidence.

## Installer checksum

SHA-256 for `WyspaSetup-0.9.1-win-x64.exe`:

```text
74f729fe18fbf3e3141fa9fdd4794fefbdfc350408913ccfac5dc5f03d6be82d
```

The release also includes `SHA256SUMS-0.9.1.txt`.
