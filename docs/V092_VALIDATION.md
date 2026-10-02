# v0.9.2 streaming responsiveness and waveform validation

This is a local follow-up build to the published v0.9.1 release. It tunes streaming latency and overlay lifecycle without changing the two-hypothesis agreement rules, Stream Fix proofreading rules, or complete final audio pass.

## Changes

- Live attempts target a one-second start cadence, subtracting request/delivery elapsed time. Slow attempts have a 100 ms minimum next delay. Snapshots require 0.8 seconds of new audio; repeated calls with no new audio do not upload again.
- Captured speech drives waveform colour independently of request state. Speech is animated red, with a 250 ms hold between voiced samples. Quiet active listening and pending work use animated green. Both states animate independently of microphone callbacks. Idle monitor activity after recording stops cannot override processing colour.
- Delivery completion hides the waveform before observer/file cleanup. Error/recovery notices have no waveform bars, so their existing notification timeout does not look like unfinished processing. Cleanup failures still report a finished-with-error message and release the output queue.
- Browser text providers may report a selection shortly after acknowledging Select. Correction now waits up to 200 ms for the same exact selection, checking input, focus and surrounding text throughout; it never reselects or injects into an unconfirmed range.
- Focus-event registration and removal now run serially on one dedicated background MTA thread, following [Microsoft's UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading). Event handlers retain their session-specific guard so delayed old callbacks cannot invalidate a new session. Native keyboard/mouse guards and exact text-range verification remain in place.

## Measured results

The accepted v0.9.1 harness and tuned harness sent the same 10.14-second synthetic speech recording to real Groq, sequentially on the same host/connection:

| Observation | v0.9.1 | v0.9.2 |
|---|---:|---:|
| First confirmed text | 5.033 s | 2.201 s |
| Updates before capture stopped | 2 | 9 |
| Final recognition after capture stopped | 0.234 s | 0.240 s |

First confirmed text arrived about 56% sooner in this comparison. Final transcripts and delivered previews were identical, including all sentence endings. This measures the real recognition/session pipeline with synthetic paced audio and an in-memory publisher; it excludes physical microphone detection and native editor insertion latency. It is one paired fixture, not a latency guarantee or a broad accuracy assessment. More frequent snapshots increase Groq request usage. [Groq documents a 20 RPM free-plan Whisper limit](https://console.groq.com/docs/rate-limits); faster updates therefore depend on the account quota. Existing Retry-After backoff remains active, and audio is retained locally through delayed requests. The paired fixture did not hit rate limiting.

A native WPF insertion run measured one UI-thread observer teardown at **6036.7 ms** before the threading correction. After moving registration/removal to the dedicated thread, all measured UI-thread EndStream calls in the successful follow-up run returned within **0.4 ms**. These measure return to the UI, not completion of background unregistration. The overlay also hides before cleanup starts, so actual final recognition can remain visible while unrelated teardown cannot prolong the busy display.

Evidence: `artifacts/v092-qa/performance-comparison.json`, `baseline-groq/live-stream.json`, `stream-groq/live-stream.json`, and `verified-stream-insertion/teardown-ms.txt` under that QA folder.

## Verification and boundaries

- **160 automated tests passed, zero failed or skipped**, with native FLAC enabled. New checks cover subsecond snapshots retaining agreement and avoiding duplicate uploads, request-time-aware cadence, hiding before native/disk cleanup, and cleanup failures releasing the output queue.
- Native WPF overlay checks cover speech overriding pending work with red, animation between audio callbacks, speech expiry returning to green, post-capture monitor levels being ignored, busy-state persistence and SmartListen notification protection.
- Native insertion tests cover WPF, Word and Edge textarea/contenteditable destinations after moving focus subscriptions to the background thread, including protected context, manual changes, session boundaries and clipboard recovery.
- A browser regression initially exposed an asynchronously reported selection, producing conservative clipboard fallback. The bounded exact-selection wait addressed this without relaxing ownership checks.
- An exploratory insertion test exposed a race between the harness's result-file writer and reader. The child now retries a busy result file instead of exiting or marking an unwritten result as delivered. This is test-only IPC; the successful native runs are the acceptance evidence.

Physical microphone accuracy, long dictations, all editors, custom hotkeys, acoustic SmartListen retrigger, DPI/multiple displays and installer install/upgrade/uninstall still require hands-on acceptance. Final complete-audio recognition may continue after the live text looks complete; the green waveform correctly remains while that accuracy check is pending.

Final acceptance evidence is in `artifacts/v092-qa/accepted-stream-insertion`, `accepted-stream-word`, `accepted-stream-browser` and `accepted-render`. Their successful native results and the application DLL hash manifest correspond to the final package, including the browser selection-confirmation fix. The binding-error log is empty.

## Installer

Inno Setup compiled successfully. The local installer is **142,618,520 bytes**, available at `artifacts/installer/WyspaSetup-0.9.2-win-x64.exe` and `%USERPROFILE%\Desktop\WyspaSetup-0.9.2-win-x64.exe`, with `SHA256SUMS-0.9.2.txt` beside each copy. Both copies match the compiler output.

SHA-256: `d25eadc62e95df5800a232bba317f22bf43c4a1cb795bb6cadd14ddfd8a53e8f`.

The 43-file payload excludes test harnesses, keys/settings and debug/audio artifacts. Application/Core/Infrastructure DLL hashes match the native harness and published payload; the packaged apphost points to `Data\Wyspa.dll`. See `artifacts/v092-application-hashes.json`, `artifacts/v092-payload-manifest.txt` and `artifacts/v092-installer-build.log`.

This local 0.9.2 candidate has not been committed, tagged or uploaded. Version 0.9.1 remains the published GitHub release.
