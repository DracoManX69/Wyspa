# Stream Mode and Stream Fix in v0.9.1

Enable **Settings → Audio & Capture → Capture & Shortcuts → Stream Mode**. It is off by default and works independently of Toggle, Hold to Talk and SmartListen. Changes take effect on the next recording. `StreamModeEnabled` remains the persisted identifier.

Enable **Settings → Experimental → Stream Fix** to proofread the final transcript. This separate switch is off by default, saves as `StreamFixEnabled`, and is disabled while Stream Mode is off. Turning Stream Mode off remembers the Stream Fix preference for next time. Stream Fix uses the existing Tone Re-write model selection, with its own fixed, conservative rules. Tone Re-write need not be enabled, and its custom prompts are not used by Stream Fix.

## What the user sees

Words appear in groups as speech is recognized. Each delivered update puts the whole current dictation on the clipboard. At the recording's end, a complete audio pass produces the authoritative final clipboard text, including recognition corrections and, when enabled, Stream Fix edits. A new recording gets a fresh cumulative transcript when its output begins.

SmartListen's Silence Before Stop, Toggle stop and Hold release still end their respective recordings. Pauses within a manual recording do not create a new clipboard session. There is no separate half-second phrase-finalization timer.

The overlay remains visible throughout outstanding recognition, proofreading and delivery. Blue/teal animated bars and “Transcribing” show processing; red bars show microphone levels while listening between requests. A busy overlay is not replaced by the brief SmartListen on/off notification. It disappears after delivery completes, or shows a completion/fallback/error message before hiding. A timeout or failed final request is reported instead of presenting a partial result as complete.

SmartListen restarts its local monitor after capture drains, without waiting for the provider. A half-second local pre-roll preserves the speech that triggered capture. Subsequent recordings can collect audio while the previous session finishes; provider work and text/clipboard delivery stay ordered. This avoids the previous network-length listening gap. Device startup still has a finite transition time and needs microphone acceptance testing.

## Safe text ownership and correction

The insertion point is established when the first text is ready. Existing selections, password fields and read-only fields are rejected when detectable. The destination, text before and after the dictation, and collapsed caret are verified before appending. Native input delivery is read back. Manual typing, navigation keys, mouse clicks or changing focus relinquish field ownership for that session, even if the cursor returns. The clipboard continues to receive the transcript.

With **Stream Mode on**, words appear live. With **Stream Fix enabled**, the final cleaned transcript replaces only the passage emitted during that session, using the configured Paste or Type insertion method. With **Stream Mode off**, the existing pipeline transcribes and cleans the recording, then inserts its result once; Stream Fix is inactive.

Final replacement uses a shared Windows UI Automation path across editors that expose reliable text ranges and selections. Wyspa verifies the exact full passage, its unchanged surrounding text and the collapsed caret, locates the complete passage ending at that caret, selects only that range and checks the selection again before sending the replacement. Matching only the first and last words is insufficient. It never selects the entire document, sets the whole field value, or counts backspaces. The clipboard and proofreading input come exclusively from Wyspa's own transcript for this session. Surrounding field content is used only for local verification and is never included in cleanup.

Any manual typing, cursor navigation, click, focus change, changed context or unverifiable selection causes clipboard fallback. Applications without usable text/caret information use clipboard-only output. A fallback message means **review the final clipboard text before pasting**: inserting it beside the live version can duplicate text. App-controlled formatting, autocorrection and document changes can also cause conservative fallback. Each side of the caret is limited to 500,000 characters for local verification.

UI Automation selection and native input are separate operations; Windows does not offer an atomic cross-application text-range replacement API. Wyspa checks ownership immediately before its input batch and verifies delivery afterwards, but cannot guarantee compatibility with every editor or concurrent document change. Undo behaviour belongs to the destination editor; there is no longer a Word-specific replacement adapter or custom Undo record.

## Conservative proofreading

Groq proposes small edits to exact, unique source excerpts. Local validation permits capitalization/punctuation, obvious um/uh/erm hesitations, a narrow set of adjacent function-word stutters, and basic is/are, was/were, has/have, do/does and a/an agreement. The validator rejects ambiguous/overlapping edits, broader lexical substitutions, changed numbers or symbols, removed negations or uncertainty, and reordered content. Meaningful emphasis such as “very very” is preserved. These rules currently favor English; other languages still receive speech recognition but little grammar editing.

This is intentionally less powerful than a general grammar checker or Tone Re-write. Complex sentence repairs, ambiguous fillers, name/spelling substitutions and stylistic rewrites are left alone. Model proposals are not guaranteed to be linguistically correct; the feature remains experimental. If proofreading fails or proposes unsupported edits, the recognized transcript is retained, with any independently validated edits only. Recording completion never executes spoken actions or applies Tone Re-write prompts.

## Recognition, requests and temporary audio

The [Groq speech-to-text endpoint](https://console.groq.com/docs/speech-to-text) recognizes completed audio uploads. Wyspa implements near-live output with snapshots, rather than a native bidirectional word stream. The worker waits 2.2 seconds between completed live attempts. Consecutive hypotheses must agree before a prefix is typed, so the first stable update normally needs two requests. Actual latency depends on speech and network conditions; speed is secondary to completeness.

Live snapshots are bounded to 20 seconds with overlapping context. They do not trim a phrase at a brief pause. The disk-backed 16 kHz mono PCM spool retains all captured audio, including the last microphone callback. The final pass re-recognizes the complete audio, independent of the live transcript's timestamp cursor. Recordings up to two minutes fit in one final upload (about 3.84 MB). Longer recordings use two-minute windows with four seconds of shared context; each window owns half the overlap by word midpoint. Timestamp shifts near these long-recording boundaries remain a recognition limitation to exercise in acceptance testing.

Final results may differ from live previews. Confirmed final tail words are appended only if the final hypothesis confirms every previously emitted word; other revisions need verified Stream Fix replacement or manual clipboard review. Low-volume samples and pauses are retained in the final audio pass. The configured speech model, language and custom vocabulary prompt are reused.

Overlapping snapshots and the complete final pass consume more Groq requests/audio allowance than ordinary dictation. Groq documents a ten-second minimum billed duration per speech request. Stream Fix adds bounded text requests using the selected cleanup model. A provider error keeps capture running locally until stop and reports an incomplete final result when necessary. No automatic model switching occurs.

Only activated dictation uploads audio. Settings previews remain local and do not create/upload recordings. Temporary snapshots and the spool are removed after completion or handled failure; the original WAV follows Retain Audio for Debugging. Abrupt process termination can leave temporary files. The live clipboard intentionally replaces its previous contents and is not restored. Conversation, YouTube and Audio Files keep their existing workflows.

Windows API references: [UI Automation text range search](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.findtext), [range selection](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.select), and [native input delivery](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
