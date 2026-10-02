# WyspaFluent v0.8 validation

Release tag: `v0.8`. Application/file version: `0.8.0` / `0.8.0.0`.

## Completed checks

- Release build and framework-dependent win-x64 publish succeeded.
- `WYSPA_FLAC_PATH=<native FLAC 1.5.0> dotnet test Wyspa.slnx -c Release --no-restore`: **109 passed, zero failed, zero skipped**. The earlier run without a native encoder skipped three cases; those were rerun with the bundled-source FLAC build and passed.
- Native Windows harness `render` completed with no `error.txt` and a zero-byte `binding-errors.txt`. It checked all main destinations, settings order and deep links, independent input-level preview with SmartListen disabled, persisted themes, simulated system-theme changes, overlay settings, model persistence, separate Conversation/YouTube libraries, and capture lifecycle using fakes.
- Framework-dependent release payload contains the app, native libraries, FLAC encoder/source/license, video utilities, speaker models, and Octicons license. It excludes debug symbols, test harnesses, and personal configuration/key files.
- Inno Setup 6.7.3 compiled the production installer. The compiler consumed the packaged `Wyspa.exe` + `Data` layout.
- `installer/tests/RuntimeProbe.iss` shares production prerequisite code and checks stable 10.0 version acceptance, preview/other-major/malformed rejection, missing-host rejection, actual installed x64 runtime detection, and success/cancellation/failure/restart return handling.
- On Windows, `runtime-prerequisite.ps1` resolved stable release metadata and verified Microsoft's 10.0.12 x64 Desktop Runtime download against its SHA-512, Authenticode signature, and product metadata. Flipping a byte caused verification to fail; the executable was never run during this test.
- Release asset download and SHA-256 comparison are performed after publishing.

Local evidence is under `artifacts/v8-qa`, `artifacts/v8-full-tests.log`, `artifacts/v8-installer-build.log`, `artifacts/v8-installer-tests`, and `artifacts/v8-runtime`. Generated artifacts and fake test data are not committed or included in the installer.

## Remaining manual acceptance

The test host already has .NET Desktop Runtime 10.0.11 installed. No installed shared runtime was removed to simulate a clean machine. The live missing-runtime download/elevation/install/reboot path has not been exercised end to end. The read-only prerequisite harness and helper checks do not substitute for that test.

Before claiming complete installer lifecycle acceptance, test a clean Windows x64 VM without .NET, allow/cancel UAC, interrupt the download, test offline retry, verify restart handling if required, upgrade a previous Wyspa installation with saved settings, and test both uninstall data choices. Runtime uninstall must remain independent of Wyspa uninstall.

Real microphone capture, foreground-app insertion, active calls, speaker identification, and live Groq requests remain manual acceptance checks. UI smoke tests use fake services and do not upload audio.
