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

## v0.9 Stream Mode acceptance

- Verify the separate Stream Mode switch under Capture & Shortcuts is initially off after upgrading and saves independently of all three activation modes. Toggle during recording and confirm it applies only to the next recording.
- In Toggle, Hold to Talk (including held Ctrl/Alt/Space and F13), and SmartListen, dictate “This is a test of Wyspa Stream mode.” Confirm words appear before Stop and before Silence Before Stop elapses. Inspect clipboard updates from a second non-focus-stealing observer; each must contain the entire delivered dictation.
- Test short answers, deliberate repetitions (“very very good”), names, punctuation, multilingual text and uninterrupted speech longer than a minute. Listen for missing/duplicated boundary words and compare first-word/end-of-speech latency and accuracy with Stream Mode off.
- Test in Notepad, Word, browser textareas/contenteditable, Teams/Slack and a terminal. Preserve surrounding text and selections. Change focus mid-stream and verify clipboard fallback, then restart dictation in the new field. Password/read-only fields must not receive injected text where detectable.
- Hold the hotkey while text arrives, release during a request, and press Stop during retry/backoff. Confirm the final tail appears once and no old response types into a subsequent session.
- Pause longer and shorter than the SmartListen silence setting. Confirm pauses during Toggle/Hold keep that recording's cumulative clipboard; a new SmartListen recording starts a fresh clipboard transcript.
- Test microphone failure, offline/network loss, invalid key, 429 rate limiting and a temporarily locked clipboard. Previously delivered text should remain available, failures should be visible, and the remaining audio must not be presented as successfully transcribed.
- Confirm Stream Mode bypasses spoken actions and rewriting without changing their saved settings. Turn it off and verify normal paste/type, clipboard preference and processing resume.
- Test upgrade from v0.8, restart, and uninstall data choices on an isolated Windows profile. Conversation, YouTube, file transcription and local level preview must retain their existing behavior.

## Consolidated settings and separate libraries

- Confirm only Home, Audio Files, Conversation, YouTube, and Settings appear in the sidebar. Open each settings group at 880×740 and 560×480 and with keyboard navigation.
- Save/test a key, then Refresh models. Transcription must list only speech recognition models; summary, rewrite, and spoken action dropdowns must exclude speech generation and moderation. Test missing/rejected keys, offline/timeout, an empty list, a retired selection, and repeated refresh. Previous choices must survive failures.
- Change each model and conversation capture setting, close normally, and reopen. Verify the next request uses the selected model; refresh must not reset it.
- Configure microphone, computer call / in-person, output device / calling app, and timing in Settings. Start, pause, resume, and stop from Conversation and its overlay. Configuration must remain locked during capture where changing it would affect the session.
- Open a saved video while recording a conversation. Video browsing, summaries, errors, and deletion must not alter the active conversation or overlay. Repeat with legacy mixed saved files and after reopening.
- Check YouTube's empty state contains no conversation or speaker instructions. Model controls must appear only in Settings, including when the conversation overlay is open. Transcription and summary models belong in Groq; re-write and spoken-action models belong in Experimental beside their feature controls. Overlay belongs in System.

## v0.9.1 acceptance: Stream Fix and complete streaming audio

- Turn Stream Mode off/on and confirm Stream Fix is disabled/enabled while remembering its saved preference. Check restart persistence and all three activation modes.
- Speak sentences with 0.5–2 second pauses and a quiet last word. Check the final clipboard against what was spoken. Repeat for more than two minutes, including repeated words across a chunk boundary.
- In SmartListen, start speaking again while the previous sentence is still transcribing. Verify onset, ordered output and that an older result never replaces a newer clipboard session.
- In Word, native textboxes and browser textareas/contenteditable, dictate between existing prefix and suffix text. Enable Stream Fix and leave the caret untouched; check only the current dictation changes, including when the same words exist elsewhere. Test both Paste and Type. Check the editor's Undo behaviour.
- Move the caret, select existing text, type, click another field, edit the prefix/suffix, switch documents or turn Track Changes on. Confirm no automatic final replacement and use the corrected clipboard for recovery.
- Test browser, Codex, Teams and other editors: live typing and final replacement depend on exposed caret/text information. Unsupported or changed fields must receive clipboard fallback without a destructive selection. Review clipboard differences before pasting to avoid duplicates.
- Verify names, numbers, dates, negations and uncertainty survive cleanup. Try fillers, stutters and deliberately repeated emphasis. Reject any meaning change during acceptance.
- Under network delay/rate limit or proofreading failure, confirm the blue processing animation remains until work finishes or an error is shown. Toggle SmartListen off while processing and confirm the processing indicator remains.
- With Stream Mode off, verify the field receives the finished transcript once and the remembered Stream Fix preference has no effect.
- Test custom hotkeys, rich Word documents, high-DPI/multi-monitor placement and physical microphone devices. These require user acceptance beyond the automated fixture checks.

## v0.9.2 responsiveness and waveform

- Compare first visible words with the same spoken phrase and provider connection, with Stream Fix off. Check that names, repeated words, sentence endings and quiet speech remain accurate.
- Speak during a slow request: the waveform must remain animated red. Pause: it should become animated green within about 250 ms while work continues. Resume speech during processing: red must take priority.
- Test Toggle, Hold to Talk and SmartListen, including a new SmartListen recording while the previous one finishes.
- After Stop, idle-monitor audio must not turn the processing waveform red. It must remain green through the final recognition and any proofreading/delivery, then disappear at completion.
- Trigger clipboard fallback: show the recovery message without a lingering waveform. Validate silent periods and network failures do not leave a permanently busy overlay.

## v0.9.3 follow-up

- Look & Feel: turn Windows notifications off; trigger connection/transcription errors and verify no Wyspa Windows notification appears. Confirm the recorder/in-app messages remain. Restart and verify the preference persists; re-enable and verify delivery resumes.
- Change Windows accent while Wyspa is open in Light, Dark and System; verify controls/navigation update and high contrast remains readable. Inspect the header and rail dividers at different DPI settings.
- Stream into editors with and without accessible text ranges. Confirm live input needs no review/paste step. Click away mid-session and confirm new text stays on the clipboard without entering the new field; start a fresh session to use the new target.
- With Stream Fix enabled, verify owned dictated passages can be corrected while existing document text stays intact. Unsupported/changed ranges should retain live output and the final clipboard backup without a review prompt.

## v0.9.4 help and appearance

- Hover each help icon, including its padding, and verify readable help appears. Click or tab to a question mark and press Enter/Space; dismiss with Escape or by moving focus. No empty question marks should appear.
- Check hints on settings controls, including disabled Stream Fix and experimental controls. Important prerequisites should also remain in inline text; tips should wrap instead of extending beyond the window.
- In Light, Dark and System, confirm the header/navigation have no fixed green tint. Windows accent still controls selection and primary controls; the app icon and semantic waveform colours remain distinct.
- Check tooltip placement and keyboard focus at increased DPI, on multiple displays, and with a screen reader.
