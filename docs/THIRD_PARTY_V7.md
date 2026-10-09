# Wyspa 0.7 third-party components

Wyspa uses these unmodified upstream tools and models. They are separate files; you may replace them with compatible builds. Bundled media tools use pinned build-time downloads in `scripts/prepare-v7-tools.py`. Deno and speaker models are downloaded on demand by `OptionalDependencyStore`, pinned by URL, size and SHA-256. Notices remain in the app package.

- **yt-dlp 2026.08.19** — https://github.com/yt-dlp/yt-dlp/tree/2026.08.19. Project source is under the Unlicense; the official Windows executable includes dependencies with additional licenses. Source, licenses, and build instructions are available in that exact release's repository and release assets: https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19.
- **Deno 2.9.7** — MIT, copyright the Deno authors. https://github.com/denoland/deno/tree/v2.9.7. License: https://github.com/denoland/deno/blob/v2.9.7/LICENSE.md.
- **FFmpeg N-126947-g45f3fecca9**, shared LGPL build from BtbN autobuild-2026-09-28-13-06 — LGPL 3 or later, with separately licensed enabled dependencies. FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/45f3fecca9. Build recipes and dependency source locations: https://github.com/BtbN/FFmpeg-Builds/tree/autobuild-2026-09-28-13-06. Release: https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-28-13-06. The LGPL license is included as `LICENSE.txt`; binaries remain dynamically linked and replaceable.
- **sherpa-onnx 1.13.8** — Apache 2.0, Xiaomi Corporation and contributors. https://github.com/k2-fsa/sherpa-onnx. Its NuGet package includes the Windows native runtime and ONNX Runtime dependencies under their upstream licenses.
- **pyannote segmentation 3.0 ONNX conversion** — obtained from the sherpa-onnx conversion at https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/tree/9403a6902bb58e3d5ae8c7e77c3422de279db2e0. Original model: https://huggingface.co/pyannote/segmentation-3.0. Conversion and model provenance: https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/models.html.
- **3D-Speaker CAM++ English VoxCeleb speaker embedding model** — obtained from sherpa-onnx's speaker-recongition-models release. Original project and license: https://github.com/modelscope/3D-Speaker. These models run on your computer; Wyspa does not send speaker embeddings to a service or retain them across sessions.
- **NAudio 2.2.1** — MIT. https://github.com/naudio/NAudio. Existing Wyspa dependencies and FLAC notices remain in their respective packages and `Tools/Flac`.

The speaker test fixture used for development is from the sherpa-onnx speaker-segmentation-models release and is not included in the app package.

## Windows media-session projection

The media handling implementation uses Microsoft.Windows.SDK.NET.Ref 10.0.17763.57 / Microsoft.Windows.SDK.NET.dll and its WinRT.Runtime.dll support. Copyright Microsoft Corporation; SDK package license: https://aka.ms/WinSDKLicenseURL. These managed projection dependencies allow the application to use Windows' existing media-session API; the Windows SDK itself is not installed on the user's computer.

Wake speech activity uses Silero VAD through sherpa-onnx, downloaded with pinned byte count and SHA-256 from the official sherpa-onnx ASR-model release. Silero VAD project/license: https://github.com/snakers4/silero-vad (MIT). Wake verification uses the already attributed OpenAI Whisper Tiny English Q5 conversion through Whisper.net/whisper.cpp; its weights are not embedded in Setup.
