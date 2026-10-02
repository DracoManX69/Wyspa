# WyspaFluent v0.9 Stream Mode validation

Application/file version: `0.9.0` / `0.9.0.0`. This is a local build; no GitHub release was created as part of this work.

## Verified

- Release build and framework-dependent Windows x64 publish succeeded with no warnings or errors.
- Full suite with the native FLAC encoder enabled: **129 passed, zero failed, zero skipped**. Evidence: `artifacts/v9-tests.log`.
- Stream tests cover agreement of provisional words, final tail delivery, repeated words, overlapping audio, delayed punctuation, preserving already committed text, bounded windows through 40 seconds of continuous audio, silence, many queued phrases, sequential requests, cancellation, request failure/recovery, clipboard failure/retry, session reset, and temporary snapshot removal.
- Tests compile the real `DictationOrchestrator` and check all three activation modes. They verify text delivery while capture remains active, settings changes taking effect next session, bypassing intent/actions/rewriting, draining a slow in-flight request, fallback to clipboard, failed start/retry, and subscription/recording cleanup. Audio and Groq are simulated in these tests.
- The native Windows WPF harness passed the existing navigation, theme, input-level preview, model persistence, and separate Conversation/YouTube library checks. It also toggled Stream Mode through the real automation peer, confirmed autosave independently in every Mode, and captured light/dark/minimum-size layouts. `artifacts/v9-qa/render/binding-errors.txt` is empty.
- An isolated native Windows target received actual Unicode `SendInput` text, preserving pre-existing text. The test verified cumulative clipboard values, held Ctrl+Alt modifiers, accented/Japanese/emoji/special characters, focus-change fallback, new-session targeting and rejection of a password control. It restored the prior clipboard. Evidence: `artifacts/v9-qa/insertion/result.txt`. Earlier harness attempts timed out with synchronous per-character file writes; the final target records changes on a timer outside the input handler and passes.
- A **real Groq** request series transcribed a Windows-generated synthetic speech fixture through the production stream service. The final run used 10.14 seconds of audio paced into PCM callbacks. First delivered text: **2.782 seconds** after test start; six delivered updates; final tail: **0.250 seconds** after stopping. The cumulative transcript matched concatenated delivered deltas. Every spoken word was retained, but the first sentence-ending period was omitted. These are observations from one synthetic fixture run, not a latency/accuracy guarantee. Evidence: `artifacts/v9-qa/live-groq/live-stream.json`.
- Package inspection found 43 intended payload files and no personal settings, keys, test harnesses, debug symbols or debug recordings. The app/Core/Infrastructure assemblies match the final published build byte for byte. The package uses the established `Wyspa.exe` plus `Data` layout.
- Inno Setup compiled the production installer successfully (exit 0). `artifacts/installer/WyspaSetup-0.9.0-win-x64.exe` is 142,612,185 bytes. Copies in the artifacts directory and on the Desktop passed SHA-256 comparison against the compiler output. SHA-256: `ab59a45994b9beb1277240a22616f290afcb4ae84db3a80fc3ec0b600f255d0e`. A matching `SHA256SUMS-0.9.0.txt` accompanies each copy. Compiler evidence: `artifacts/v9-installer-build.log`.

## Remaining acceptance

Physical microphone quality, noisy rooms, real hotkey holding/release under load, diverse languages and accents, and long real-world dictations still need acceptance testing. The native typing test uses a WPF editable control; Notepad, Word, browser editors, Teams/Slack, terminals, elevated applications and unusual accessibility providers need separate testing.

Whisper is used through repeated completed audio uploads. Stream Mode therefore consumes more requests and audio allowance, and real network delays/rate limits can slow updates. Previously committed wording is append-only; punctuation or wording can differ from a single final transcription. See `STREAM_MODE.md` for the interaction and privacy behavior.

The installer must still be tested through clean installation, upgrade with existing settings/key/notes, uninstall choices and the missing-runtime elevation path on an isolated Windows profile. Building an installer and checking its payload do not establish those lifecycle behaviors.
