# v0.9.3 notifications, Windows accent and automatic streaming

This local candidate builds on the v0.9.2 latency/overlay changes. It has not been published to GitHub.

## Behaviour

- Look & Feel has a persisted Windows notifications switch, default On to preserve existing behaviour. Off gates every Wyspa tray/Windows notification at the delivery point, including errors. It does not change other applications' notification settings or hide in-app feedback/the recorder overlay.
- Semantic accents follow the user's Windows accent in all three appearance modes. Primary fills use the OS colour, accent foregrounds choose black/white by contrast, and link/selection/hover colours derive from it. High contrast retains system colour precedence. The source is the official WPF [SystemColors.AccentColor API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemcolors.accentcolor?view=windowsdesktop-10.0). Theme refresh handles general, colour, desktop, visual-style and accessibility changes. Raster logo assets retain their branding.
- The header has a one-unit bottom border; the navigation rail has a one-unit right border, both using the shared LineBrush token.
- Live stream typing no longer requires accessibility text/caret readback. A detected editable field can receive Unicode input without a TextPattern. Where available, delivery readback lets queued input settle; missing/normalized readback prevents optional final correction, not further live typing. Focus and user-input guards still stop the session from typing into another field or after manual edits. Detectable password/read-only fields and pre-existing selections remain protected.
- Clipboard fallback is a normal backup and no longer produces a review-before-pasting message or a completion error. The clipboard contains only the current dictation. Stream Fix's final replacement still requires the verified full dictated passage, so unsupported editors retain live output and get the final cleaned version on the clipboard without forced replacement.

## Validation

Automated tests: 160 passed, zero failed/skipped, with the bundled source-built native FLAC encoder enabled. Existing tests now cover notification preference persistence/default compatibility and normal completion when insertion falls back to clipboard.

Native harness coverage includes notification On/Off delivery gating via a recording sink (no OS balloons emitted), toggle autosave/reload, injected purple/gold/blue accents in Light and Dark, production readback of the host accent, and the existing appearance/overlay/navigation checks. An isolated text box intentionally withholds TextPattern to exercise automatic live typing and cumulative clipboard backup.

During testing, removal of delivery waiting exposed a Word timing race: the harness could move its selection while earlier SendInput events were still being delivered. Optional delivery settling was retained for editors with readback; missing readback does not block live typing. A transient clipboard lock also exposed immediate-read/restore assumptions in the harness; it now polls for the new backup and retries clipboard restoration.

Physical microphone use, every third-party editor, actual OS accent-change notification delivery, high contrast, screen readers, DPI/multi-monitor use and installer install/upgrade/uninstall require hands-on acceptance. The notification gate is exercised without posting real notifications to the user's desktop.

## Final evidence and installer

- Native WPF insertion (including the field without TextPattern), Microsoft Word, Edge textarea and Edge contenteditable passed. Evidence: `artifacts/v093-qa/stream-insertion`, `stream-word`, and `stream-browser`.
- Final rendering/settings harness passed in `artifacts/v093-render-qa/render`; binding-errors.txt is empty. Light, Dark and minimum-size captures were visually inspected. The earlier render attempt exercised a collapsed setting before it entered the visual tree; the final test opens Look & Feel first and then toggles the real control. The harness now also propagates render failures via its exit code.
- App/Core/Infrastructure hashes match the tested native and rendering harnesses and final packaged payload: `artifacts/v093-application-hashes.json`.
- Inno Setup compiled successfully. The 43-file payload excludes test harnesses, keys/settings, debug symbols and captured audio. See `artifacts/v093-payload-manifest.txt`.
- Installer: `artifacts/installer/WyspaSetup-0.9.3-win-x64.exe`, also copied to the user's Desktop. Both copies match, 142,622,190 bytes.
- SHA-256: `f85ecf1f58b3f31b913f14dc911bf484ca5263212e182aeaa8ea27889e165ccf`. Checksum files are beside both copies.

No GitHub release, tag, push or installation was performed for this candidate.
