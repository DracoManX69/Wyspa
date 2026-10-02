WyspaFluent brings a refreshed Fluent-inspired interface to Wyspa, with clearer settings groups, more readable light and dark themes, and simpler setup guidance.

## What's changed

- Refined navigation, cards, controls, spacing, and subtle separation between settings subsections.
- Home now includes Getting Started instructions for creating a Groq account, obtaining an API key, and connecting Wyspa.
- AutoCapture is now called **SmartListen**. **SmartListen Threshold** shows microphone levels before you enable automatic capture.
- **Audio & Capture** uses **Input Device** for microphone selection.
- **Look & Feel** groups the saved **Dark / Light / System** theme choice with overlay and dictation appearance/behaviour settings.
- Conversation includes a shortcut to its settings; Experimental is last in the settings list; About links to this repository from the navigation footer.
- Existing settings identifiers, notes, encrypted API key storage, and installer application ID are preserved.

## Install or update

Download **WyspaSetup-0.8.0-win-x64.exe** below and run it. You can install over an existing Wyspa installation without uninstalling first. The default per-user directory remains `%LocalAppData%\Programs\Wyspa`; personal data remains in `%AppData%\Wyspa`.

The installer checks for **Microsoft .NET 10 Windows Desktop Runtime x64**. If it is missing, it downloads the latest stable compatible 10.0 servicing release directly from Microsoft, verifies its SHA-512 and Microsoft signature, runs the runtime installer, then continues installing Wyspa in the same wizard. Allow the Windows administrator prompt for this prerequisite. Wyspa itself installs per-user. An existing compatible runtime skips the download; installing a missing runtime requires internet access and administrator approval.

Cancellation and download/install failures provide a retry. If Microsoft reports that a restart is required, follow the restart prompt. The shared Microsoft runtime stays installed when Wyspa is uninstalled.

The GitHub tag is **v0.8**; Windows file/application version is **0.8.0**. SHA-256 checksums are supplied in the second release asset. The Wyspa installer is unsigned; verification of Microsoft's runtime download is separate from signing the Wyspa installer.

## Validation and limits

- 109 automated tests passed, with no failures or skips, including FLAC integration tests.
- Native Windows WPF smoke checks passed with zero binding errors, covering navigation, settings, theme persistence/system following, overlay controls, separate libraries, and the independent microphone preview lifecycle using fake capture.
- The shared Inno prerequisite code was tested for installed-runtime detection, accepted/rejected versions, and success/cancellation/failure/restart result handling.
- The runtime helper resolved Microsoft's current stable metadata, verified a real Microsoft runtime download, and rejected a corrupted copy.
- The production installer was compiled with Inno Setup 6.7.3 and the published asset was checked against its SHA-256 checksum.

A clean Windows machine without .NET was not available for a complete prerequisite installation/UAC/reboot test. Full install/upgrade/uninstall lifecycle checks and real microphone/call/Groq acceptance remain manual checks; the automated UI tests use fakes and do not claim real audio acceptance.
