# v0.9.4 tester polish

Validated installer for the v0.9.4 GitHub release.

## Changes

- Replaced all 27 question-mark border adornments with the shared HelpButton. The old borders lacked a complete hover background and were not keyboard/click controls. HelpButton provides a 24-unit hit area, keyboard focus, an accessible name, and the same help on hover or explicit invocation. Blank help is collapsed automatically.
- Help closes with Escape, focus loss, clicking elsewhere, window deactivation, unload, or a 20-second timeout. Hover uses WPF's tooltip service. Popups wrap within 380 units, use theme resources, and follow a 400 ms initial / 100 ms between-tooltip timing policy.
- Added concise explanatory hints to all authored Settings inputs/actions and the main Audio Files, Conversation, YouTube, Scratchpad and conversation-overlay actions. Hints explain prerequisites and the different scope of Stream Mode, Stream Fix and non-streaming cleanup. Disabled controls retain hover guidance. Ordinary settings use their control tooltip/inline note without adding another question mark to every row.
- Header/navigation/scratchpad chrome is neutral grey: #E9E9E9 Light and #202020 Dark. The Windows accent still drives controls and selection. Raster icon branding and semantic recording/processing/error colours keep their distinct roles. Startup accent resources also use the OS accent instead of fixed teal.

## Verification

- Automated suite: **160 passed, zero failed/skipped**, with native FLAC enabled (`artifacts/v094-tests.log`).
- Windows WPF harness passed with an empty binding-error log (`artifacts/v094-final-qa/render`). It loads the actual settings/views using isolated fixtures.
- The help audit verified all **27 named help buttons** and **85 authored Settings controls including those buttons** have guidance. It checks disabled-hint configuration, actual hover over transparent padding, Button automation Invoke, real Space/Enter activation, Escape, focus dismissal and empty-help removal. Evidence: `help-validation.txt` and `help-hover.png`.
- Theme checks verify neutral RGB channels for chrome in Light/Dark with different injected Windows accents; existing real-host accent readback, appearance preference persistence, notification gating, overlay and navigation checks also pass.
- Light, Dark and narrow client renders were visually inspected. The helper popup wraps correctly and the header/navigation tint is removed. Captures do not include OS caption composition.
- An initial native load detected an unavailable named slider style. Sliders now receive the tooltip policy without replacing their native template; the successful final harness contains this correction.

This is UI polish; no new physical microphone, Groq, editor insertion or installer lifecycle acceptance was performed. Prior streaming behaviour is retained. Multi-monitor/DPI placement, high contrast, screen-reader use and install/upgrade/uninstall remain manual checks.

## Installer and final evidence

Inno Setup completed successfully. Installer: `artifacts/installer/WyspaSetup-0.9.4-win-x64.exe`, also copied to the user's Desktop. Both copies match: **142,626,412 bytes**.

SHA-256: `488b5f26699c278681fba3a0e56847454edb60d78e510b69d2ca0a4f7c9e3bf4`. Checksum files are beside each copy.

The 43-file payload excludes test harnesses, keys/settings, debug symbols and captured audio. App/Core/Infrastructure hashes match the final native harness and packaged payload. The apphost targets `Data\Wyspa.dll`. See `artifacts/v094-application-hashes.json`, `artifacts/v094-payload-manifest.txt`, `artifacts/v094-hint-audit.json` and `artifacts/v094-installer-build.log`.

Final native evidence and captures: `artifacts/v094-final-qa/render`. The successful final run includes the clarified non-streaming cleanup/command hints. Release publication is recorded under the v0.9.4 GitHub tag. Installer lifecycle testing was not performed.
