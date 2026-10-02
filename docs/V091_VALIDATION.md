# v0.9.1 Stream Fix validation

This record covers the accepted v0.9.1 Windows installer prepared for GitHub release, with generic dictation-range correction replacing the earlier Word-only adapter. The release publishes the tested installer and its checksum together.

## Automated and native evidence

- Release build: .NET 10, win-x64, framework dependent; Inno Setup retains the existing .NET Desktop Runtime prerequisite installer.
- Full suite with the bundled native FLAC encoder enabled: **155 passed, zero failed, zero skipped**. Evidence: `artifacts/v091-revised-tests.log`. The additional regressions verify Stream Mode off inserts exactly once after capture in Toggle, Hold to Talk and SmartListen, without running Stream Fix. Streaming also preserves the insertion method selected at recording start.
- Core/orchestrator checks include all three activation modes, full final audio across pauses, bounded overlapping long-recording uploads, complete trailing audio after cancellation, queued capture while finalization is blocked, ordered clipboard ownership, cleanup failure recovery and drain/start exclusion.
- Proofreading tests reject changes to names, vocabulary, quantities, dates, negation, uncertainty and meaningful emphasis; validate conservative filler/stutter/agreement edits; and reject ambiguous, overlapping and malformed proposals.
- Native desktop **Microsoft Word** uses the same UI Automation and native input path as other editors. It passed live insertion, final passage replacement, prefix/suffix preservation, corrected clipboard, native Undo in the tested Paste scenario, caret-move and prefix-edit rejection, and pre-existing selection protection. Word COM is used only by the test to create and inspect its own document. Evidence: `artifacts/v091-revised-qa/word/result.txt`.
- A separate **WPF process** verified both Paste and Type final replacement, Unicode/surrogates, held Ctrl+Alt during live input, cumulative clipboard, repeated words elsewhere in the field, prefix/suffix preservation, filler-only deletion, external context edits, cursor movement away and back, focus changes, pre-existing selections, session reset and password protection. Evidence: `artifacts/v091-revised-qa/insertion/result.txt`.
- Real **Microsoft Edge** checks use isolated browser profiles and local HTML, without forced accessibility flags. Textareas exercise Paste and Type correction across consecutive sessions; a contenteditable field exercises live input and final correction with surrounding text. Evidence: `artifacts/v091-revised-qa/browser/result.txt`.
- Native WPF rendering exercised the real Stream Fix toggle dependency, autosave and remembered preference, existing settings/navigation/theme/model/library checks, and the busy overlay surviving beyond its old 2.2-second timer. It verified a changing processing animation and a SmartListen toggle notification not replacing the busy state. Light/dark/narrow views were rendered and inspected. Binding-error output is empty.
- Earlier in this v0.9.1 work, a real Groq test paced **10.14 seconds of synthetic speech** through the production streaming service. First confirmed output arrived at **5.006 seconds**; four updates retained all fixture words and sentence-ending punctuation. Complete final recognition finished **0.295 seconds** after capture stopped. These timings exclude the separate proofreading test and are observations from one fixture, not a latency guarantee. Evidence: `artifacts/v091-qa/live-groq/live-stream.json`.
- Earlier in this v0.9.1 work, a real Groq proofreading request using `openai/gpt-oss-20b` changed “um I I think this is a test and the results is ready” to “I think this is a test and the results are ready”, with no rejected edits. Evidence: `artifacts/v091-qa/live-groq/proofread.json`.
- Current provider discovery returned `openai/gpt-oss-20b` as available; the previous default `llama-3.1-8b-instant` was not returned. New settings now default to the current model. Existing saved model selections remain intact. Missing-model errors still preserve recognized text.
- The 43-file production payload excludes test harnesses, settings, encrypted keys, debug symbols and temporary recordings. Application/Core/Infrastructure DLL hashes match those exercised by the native harness. The packaged apphost's `Data\Wyspa.dll` path was verified. Evidence: `artifacts/v091-revised-payload-manifest.txt`. The live Groq fixtures were not rerun for this insertion-only revision; speech recognition and proofreading logic are unchanged.

## Scope and remaining acceptance

Automatic final correction is available in editors with verifiable Windows UI Automation text ranges. Word, native WPF textboxes, Edge textareas and a basic contenteditable field are covered by native tests. This does not establish compatibility with every browser editor, Codex, Teams, terminal or rich document. Input/focus changes or unavailable ownership information leave the final dictation on the clipboard. Field content is never used as proofreading context. UI Automation selection and input delivery are separate operations, so this remains a conservative experimental feature rather than an atomic cross-application editing guarantee.

No physical microphone test, long real-world dictation, actual SmartListen acoustic retrigger, custom-hotkey coverage, rich/collaborative Word document coverage, installer install/upgrade/uninstall cycle, high-DPI or multi-monitor acceptance was performed. The packaged application DLLs are matched to the tested native build; compiling an installer does not establish installer lifecycle acceptance.

Conservative proofreading favors English and deliberately leaves larger sentence repairs alone. Long final audio windows use timestamp midpoints to divide shared context, which can still be affected by recognizer timing shifts. Model recognition itself can still be wrong. Use the acceptance checklist in `MANUAL_QA.md` with actual speech and normal apps.

Exploratory runs are separate from the successful evidence above. The revised harness verifies Word's isolated foreground document before emitting input, tolerates asynchronous clipboard availability, and preserves visible whitespace in its browser rich-text fixture. Focus/ownership failures keep clipboard fallback. The obsolete text-model failure and provider discovery belong to the earlier v0.9.1 validation. Production focus callbacks now capture their own session guard so delayed notifications cannot affect a later recording.

## Installer handoff

Inno Setup compiled the revised installer successfully (exit 0). It is **142,624,908 bytes**. The compiler output, `artifacts/installer/WyspaSetup-0.9.1-win-x64.exe`, and `%USERPROFILE%\Desktop\WyspaSetup-0.9.1-win-x64.exe` have identical SHA-256 hashes. `SHA256SUMS-0.9.1.txt` is beside each delivered copy.

SHA-256: `74f729fe18fbf3e3141fa9fdd4794fefbdfc350408913ccfac5dc5f03d6be82d`.

The earlier v0.9.1 installer is preserved in `artifacts/installer/previous-v091/`. The Desktop v0.9.0 installer remains untouched. No installer was run against the user's installed profile.

Evidence: `artifacts/v091-revised-installer-build.log`, `artifacts/v091-revised-payload-manifest.txt`, and `artifacts/v091-revised-application-hashes.json`. The 43-file payload's application/Core/Infrastructure DLLs match those exercised in the successful native tests; its apphost points to `Data\Wyspa.dll`.
