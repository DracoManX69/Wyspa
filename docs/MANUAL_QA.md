# Audio Files — 0.6.1

- Open Audio Files at default/minimum window sizes and in light/dark mode; scroll to transcript and copy/save controls.
- Choose multiple local WAV/AIFF/FLAC/MP3/M4A/OGG/WebM files, including spaces/non-ASCII names. No request should occur before Transcribe.
- With no saved key, Transcribe should explain how to connect without preparing/uploading audio.
- Transcribe WAV: check lossless savings and a usable transcript; verify source bytes are unchanged.
- Transcribe already compressed MP3/M4A: verify no unnecessary FLAC conversion.
- Transcribe a PCM/FLAC file over 24 MB: all parts should upload in order and the final text should include the ending.
- Cancel while compressing and uploading: UI becomes available, temporary files disappear, completed/partial results remain, later queue files remain unstarted.
- Retry a batch: completed files are skipped; partial/failed files restart without duplicating old text.
- Check missing, empty, malformed, inaccessible and oversized compressed files: clear per-file errors; next queued file can still run.
- Copy the selected transcript; save it as UTF-8 text; cancel the Save dialog; test an unwritable destination.
- Toggle dictation writing cleanup and command intent: file transcription should still use only Whisper.
- Quit during work: cancellation completes and temporary files are deleted. Closing to tray should allow processing to continue.
- Install 0.6.1 over 0.6.0: verify saved key/settings survive and bundled FLAC files are available under Data/Tools/Flac.


## 0.7.0 conversation and YouTube acceptance

- Call using headphones: select microphone and output device; confirm You on right, Other side on left, and chronological ordering when API results return in a different order.
- Pause mid-sentence: both devices stop; captured tail finishes; Resume continues the same saved note with a timestamp gap.
- Stop with requests in flight; verify all results are saved. Test Cancel pending under a slow/offline connection and confirm gap markers.
- Verify dictation hotkeys, scratchpad, wake training and AutoCapture cannot start during notes, and prior media/listening state returns afterward.
- On Windows build 20348+, select a calling app and play audio from another app: only the chosen process tree should be captured. Verify app exit and restart. On build 19045, verify the unavailable-app message and working device option.
- Room: two real participants, then three, with similar voices, quiet speech, background noise and interruptions. Confirm detected speakers, choose My room speaker, and verify that selection survives new results and a full app restart. Automated fixture checks do not replace this real-conversation acceptance test.
- Overlay: move/resize, keep typing in the calling app, pause/stop, close/reopen from tray, inspect summary, test light/dark and 125%/150% display scaling.
- Summarise during recording and after Stop; choose another summary model in Settings → Groq, then test a retired or inaccessible saved model. Ensure failures retain the previous summary and original transcript. Test a long multi-part summary.
- YouTube: public video, unlisted video, Shorts, completed broadcast, long video, unavailable link, playlist, login-only video, cancellation during download and during transcription. Confirm no login/cookie use and all temporary media removed.
- Rename a saved note, restart, read TXT copy, delete selected note and verify JSON/TXT are both removed. Test unwritable notes directory and recovery using Save again.

Automated evidence for this build is recorded separately in `docs/V7_VALIDATION.md`.

## Consolidated settings and separate libraries

- Confirm only Home, Audio Files, Conversation, YouTube, and Settings appear in the sidebar. Open each settings group at 880×740 and 560×480 and with keyboard navigation.
- Save/test a key, then Refresh models. Transcription must list only speech recognition models; summary, rewrite, and spoken action dropdowns must exclude speech generation and moderation. Test missing/rejected keys, offline/timeout, an empty list, a retired selection, and repeated refresh. Previous choices must survive failures.
- Change each model and conversation capture setting, close normally, and reopen. Verify the next request uses the selected model; refresh must not reset it.
- Configure microphone, computer call / in-person, output device / calling app, and timing in Settings. Start, pause, resume, and stop from Conversation and its overlay. Configuration must remain locked during capture where changing it would affect the session.
- Open a saved video while recording a conversation. Video browsing, summaries, errors, and deletion must not alter the active conversation or overlay. Repeat with legacy mixed saved files and after reopening.
- Check YouTube's empty state contains no conversation or speaker instructions. Model controls must appear only in Settings, including when the conversation overlay is open. Transcription and summary models belong in Groq; re-write and spoken-action models belong in Experimental beside their feature controls. Overlay belongs in System.
