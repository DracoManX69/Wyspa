# Wyspa 0.7.0 validation

Validated 28–29 September 2026 on the Linux build host and Windows 10 build 19045 host. Work is on `codex/v0.7.0`.

## Automated checks

- `dotnet build Wyspa.slnx --configuration Release --no-restore`: clean, zero warnings/errors.
- Final unit suite: **95 passed, 3 skipped, 0 failed**. The three skipped cases are existing Windows FLAC execution tests. The installed Windows environment has a desktop runtime but no SDK; a cross-host VSTest attempt was unsuccessful, so those cases are not counted as passed.
- New coverage includes YouTube URL validation, ordered transcripts, overlapping speaker turns, duplicate audio boundary handling, silence/chunk flushing, summary request/model selection, hierarchical long-summary input coverage, API errors, note save/reopen/delete, invalid JSON recovery, pause/resume, room text before identification, missing API key, cancelled pending requests, capture-stop failures, temporary file cleanup, and YouTube partial-result storage.
- `git diff --check`: clean.

## Windows runtime checks

The reproducible `tests/Wyspa.WindowsSmoke` harness loads the actual app views and native services with synthetic text or the upstream speaker test fixture. The test settings and note files are isolated from normal app data; the two explicitly selected live Groq checks use the existing protected key without printing it.

- Rendered the actual main WPF window's Conversation and YouTube tabs and the floating overlay; exercised start, pause, resume, stop and saved-note reopen; **zero WPF binding errors**. Also rendered the main window at its 560 × 480 minimum size.
- Groq: a synthetic spoken test returned **33 timestamped words**, then a summary from the configurable default `openai/gpt-oss-20b`. The summary included the explicit outline and review action items.
- YouTube: `https://www.youtube.com/watch?v=jNQXAC9IVRw` downloaded publicly, converted to 16 kHz mono PCM, and returned **36 timestamped words** from Groq. Prepared media was disposed after the test.
- Local room model: the supplied 16-second two-speaker fixture yielded **four turns and two speakers**. The final rolling-context test over 3.5-second windows kept **two identified speaker labels** and explicitly marked one brief uncertain fragment. This is fixture evidence, not an accuracy guarantee for real conversations.
- WASAPI output-device loopback captured **1,330,056 bytes** of playback in the native check. This test did not upload captured microphone or incidental system audio.
- **Per-app capture could not be executed on this host**, because build 19045 predates the process-loopback API. The implementation was compiled and checked against Microsoft's ApplicationLoopback sample; actual app isolation and app-exit handling still need testing on build 20348+.

## Delivery checks

- Standard Inno Setup installer compilation succeeded for `WyspaSetup-0.7.0-win-x64.exe`; the existing runtime prerequisite remains .NET 10 Desktop Runtime.
- Self-contained `win-x64` portable build includes the .NET runtime, local speaker models, yt-dlp, FFmpeg and Deno. No Python or separate model download is needed by the end user.
- App/Core/Infrastructure assembly SHA-256 hashes match between the Windows-tested binaries, installer publish directory, and final Desktop portable copy. Required native DLLs/tools/models are present. Tool downloads are pinned and checksum-verified.
- The installer was compiled but was **not installed over the user's existing installation**. The available older innoextract cannot parse Inno Setup 6.7.3; its failure is not an installer execution result. The portable program's exact assemblies were exercised through the Windows harness; a full cold launch of its own executable with normal user settings remains a user acceptance check.
- Desktop handoff: `Wyspa v7 Portable/Wyspa.exe`, `Wyspa v7 Portable/Start here.txt`, and `WyspaSetup-0.7.0-win-x64.exe`.
- Evidence: `artifacts/v7-tests.log`, `artifacts/v7-installer-build.log`, screenshots/results under `artifacts/v7-qa`, and SHA-256 manifests there. No real keys are included in these files or release outputs.

## Remaining real-use checks

1. Real computer call with headphones: both sides, interruptions, long continuous speech, device unplug/replug, actual end-to-end delay.
2. Selected-app capture on Windows build 20348+, including another concurrently playing app and app restarts.
3. Real in-person conversation: quiet and similar voices, longer sessions, overlapping speech, and speaker-match tuning. Room identification remains explicitly experimental.
4. Long public YouTube video, different public/unlisted/Shorts URLs, upstream restrictions, and cancellation at each download/preparation/transcription phase.
5. Installer upgrade/uninstall on a disposable Windows profile, plus light theme and non-default DPI across multiple monitors.


## Settings consolidation follow-up — 1 October 2026

- Sidebar now contains Home, Audio Files, Conversation, YouTube, and Settings. Settings holds Groq (key plus transcription/summary/rewrite/action models), Conversation, Audio & capture, Dictation behaviour, Privacy, and System. Removed duplicate connection setup/key removal, the unimplemented dictation history controls, and model-ID editors in the conversation/video pages and overlay.
- Model discovery uses the existing authenticated Groq GET /models request. Active IDs are classified by known compatible model families; speech generation, moderation, and unknown families are excluded. The documented API has no task-capability field, so wholly new families may need a compatibility update. Saved selections survive failed refreshes and missing models; missing choices display a recovery message.
- Conversation and YouTube each have an independent view-model instance scoped to their own library. Legacy saved files stay in place and are filtered by Kind; selections, transcripts, summaries, errors, operations, and deletion are isolated.
- Final automated suite: **105 passed, 0 failed, 3 skipped**. The three pre-existing FLAC tests require a native Linux encoder, which was unavailable. The Windows attempt to run the Linux SDK's vstest executable failed with BadImageFormatException; this is a test-runner limitation, not an app test result.
- Native Windows WPF harness passed for all five navigation destinations, every settings group, 880×740 and 560×480 layouts, task-filtered model dropdowns, refresh preserving selection, disk persistence, independent libraries, and simulated start/pause/resume/stop. **No WPF binding errors.** Final screenshots and test report are in artifacts/settings-qa-final. This harness uses fake audio capture and fixture transcripts; real microphone/call capture was not repeated for this follow-up.
- Live authenticated model discovery succeeded: two speech models (whisper-large-v3, whisper-large-v3-turbo) and three general text models (openai/gpt-oss-120b, openai/gpt-oss-20b, qwen/qwen3.8-27b) for the saved key at verification time. This check sent only a model-list request, with no audio or transcript upload. Evidence: artifacts/settings-qa/live-models.json.
- Self-contained win-x64 build produced at artifacts/settings-portable and copied to Desktop/Wyspa Settings Preview. Executable and all seven required native tools/models have matching SHA-256 hashes (artifacts/settings-qa-final/package-verification.json). Uses the existing user settings and notes. Existing installed/released builds were not replaced or republished. The standalone bundled executable was packaged and hash-verified; rendering and interaction checks ran through the Windows WPF harness.

## Navigation icons and Experimental follow-up - 2 October 2026

- Added native vector sidebar icons: talking head with voice waves for Conversation, rounded play button for YouTube, and upward arrow/tray for Audio Files. Icon strokes and fills bind to the sidebar item's foreground, including theme and selected-state changes.
- Moved Wake Voice, Tone Re-write and its model selector, and Spoken Actions and its model selector into Settings > Experimental. Moved Overlay into System. Transcription and summary selectors remain under Groq; Refresh models continues to update all selectors.
- Release Windows build succeeded. The existing native WPF harness passed, including model selection/refresh/persistence and conversation lifecycle. Screenshots cover normal/minimum sizes, Experimental, System, and light/dark navigation. Binding-error log is empty. This change only moves existing controls and adds vector icons; no new unit tests were added.
- New evidence: artifacts/icons-experimental-qa. Self-contained build: artifacts/icons-experimental-portable. The Desktop/Wyspa Settings Preview/Wyspa.exe executable and all seven required bundled tools/models match the package by SHA-256.
- Updated the same Desktop executable using a staged file and a recoverable .bak of the previous build. The existing process was kept running; quit it through the tray and reopen Wyspa.exe to load the update. Personal settings were not changed by this handoff.

## GitHub v0.7.0 release preparation - 2 October 2026

- Fresh automated run: **108 passed, 0 failed, 0 skipped**. Built native FLAC 1.5.0 from the already bundled source archive and supplied `WYSPA_FLAC_PATH`, enabling all three lossless integration tests on the Linux build host.
- Fresh Windows WPF harness run passed for the final settings/navigation UI, model filtering/refresh/persistence, independent conversation/video libraries, and simulated conversation lifecycle. The binding-error log is empty.
- Standard framework-dependent win-x64 package built successfully, and Inno Setup 6.7.3 compiled the 0.7.0 installer. The package contains 42 files, including the required tools/models and notices. Checked app/core/infrastructure and native inference assembly hashes against the Windows-tested package; they match. No local settings, key files, test reports, or debug symbols are included.
- Release notes: `docs/RELEASE_NOTES_0.7.0.md`. Release-build evidence is under ignored `artifacts/release-0.7.0/qa`; the release assets are the setup executable and SHA-256 checksum file.
- Installer upgrade and uninstall were not performed on the user's existing installation. The real-use checks listed above remain applicable; native UI checks use fake capture and fixture notes. This release does not claim a fresh real-call or room accuracy test.
