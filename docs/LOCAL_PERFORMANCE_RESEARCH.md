# Local dictation performance research — 5 October 2026

## Choice and tradeoffs

The 0.9.5 revision adds a 45,202,074-byte Zipformer English 20M package for the included continuous-streaming path. This is the icefall LibriSpeech streaming-small model published by the Next-gen Kaldi team, with INT8 encoder/joiner and FP32 decoder. It is English-only and does not supply punctuation. Its greedy recognizer retains decoder state across audio chunks. It is a small-footprint default, not a claim of best accuracy or universal fastest inference.

Whisper Tiny English (77.7 MB) is the stronger choice on the tested RTX 3080 Ti, and was also faster for whole-file decoding on its i7-7700K after tuning. Whisper is kept in the catalog for punctuation, multilingual choices and larger accuracy-oriented models. Model selection remains the user's choice: upgrading does not replace a saved model/provider preference. Fresh profiles default to the included model and local Stream Mode; Groq remains opt-in.

Primary sources:
- [sherpa-onnx Zipformer documentation](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/zipformer-transducer-models.html#csukuangfj-sherpa-onnx-streaming-zipformer-en-20m-2023-02-17-english): provenance, quantized files, incremental inference and example RTF. Published RTF is not used as a claim about this PC.
- [Pinned model files and Apache-2.0 model card](https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17/tree/d42f2d9f7ca24806fb667456a18a9f1b60f70d16).
- [sherpa-onnx C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html): stateful online recognition. Wyspa uses the same pinned 1.13.8 package already used for local speaker processing.
- [Whisper.net 1.9.1 source](https://github.com/sandrohanea/whisper.net/tree/1.9.1): GPU context, runtime selection and decoding settings. Vulkan supplies a cross-vendor Windows route without bundling a CUDA toolkit. Installed driver support is still required.
- [whisper.cpp streaming example](https://github.com/ggml-org/whisper.cpp/tree/master/examples/stream): sliding-window inference is different from a native online transducer. GPU acceleration does not make Whisper inherently stateful streaming.

## Measured on this host

Windows, i7-7700K (4 cores / 8 logical processors), 32 GB RAM, RTX 3080 Ti. Eleven-second public JFK speech fixture, three warm repetitions after the first inference. Windows native assemblies launched from the WSL share; this can materially inflate initial loading time. No actual microphone, network Groq request, or multi-speaker/accent corpus was used.

| Path | Warm full-clip elapsed | Notes |
| --- | --- | --- |
| Zipformer 20M, 2 CPU threads | 0.91–1.11 s | Missed/misrecognized opening words; no punctuation |
| Whisper Tiny English, Vulkan GPU | 0.24–0.31 s | Expected fixture sentence |
| Whisper Tiny English, CPU forced, 4 threads | 0.67–0.70 s | Expected fixture sentence |

Zipformer streaming: 100 ms chunks fed as quickly as possible, 15 text updates, approximately 1.00 s total decoding work and 0.020 s final flush. This demonstrates incremental processing and tail cost; it does not establish microphone-to-screen latency under real-time capture. Cold first calls varied from approximately 6–20 seconds including native library/model loading; the app now prepares the selected installed engine in the background. Do not advertise one-second first launch or a universal maximum latency. Benchmark reports: artifacts/fast-validation/benchmark-*.json.

## Changes motivated by measurement

- Bundle verified Zipformer files so first use requires neither an account nor a download.
- Keep a loaded recognizer; prepare it at startup when local mode is enabled.
- Feed 16 kHz PCM directly instead of launching FFmpeg and producing a second WAV for every dictation. Other media formats still use bundled FFmpeg.
- Bound Zipformer decoding to 1–2 CPU threads, Whisper to 1–4 based on logical processors/RAM. Zipformer work dispatch uses below-normal background threads. This is a conservative resource budget, not an exhaustive automatic hardware benchmark or a guarantee about GPU utilization.
- Detect physical Vulkan adapters through the installed loader; allow GPU acceleration to be disabled. Fall back to CPU when unavailable. Include Vulkan native libraries in the installer.
- Greedy Whisper decoding, no cross-recording context, no repeated temperature-fallback passes, Flash Attention where supported.
- Use 100 ms local-online scheduling with 0.5-second maximum decoding chunks, bounded disk backlog and exact-once audio consumption. Stop flushes only the tail. Existing insertion safety and cumulative clipboard apply.
- Whisper local streaming also avoids the former complete final re-decode; it finishes its current bounded window. It remains overlapping-window recognition. Groq retains its existing network snapshot/final reconciliation path.

## Limits and follow-up acceptance

Speed and model size do not predict accent/noise accuracy. The small default needs diverse microphone/accent testing; compare optional Whisper on the user's own speech. Voice setup measures error and estimates SmartListen pause timing; it does not fine-tune acoustic weights or learn demeanor. Vocabulary prompting applies only to Whisper. A user's silence timeout adds latency independently of engine computation.

GPU detection alone does not prove every model fits every device. NVIDIA is exercised here; AMD/Intel, driver-failure behavior, low-RAM PCs, integrated-GPU contention, sustained battery/thermal impact, long recordings, and clean Windows installation still require acceptance testing. No NPU backend, CUDA dependency, OS priority change, server, Python installation or command-line setup is required from end users.

## Other lightweight candidates

The [Vosk official catalog](https://alphacephei.com/vosk/models) lists `vosk-model-small-en-us-0.15` at 40 MB with published benchmark WER. It is a credible small streaming alternative, but was not integrated or benchmarked here: sherpa-onnx is already a dependency of Wyspa, so Zipformer avoids adding a third native engine. Published Vosk and sherpa accuracy/speed numbers use different conditions and should not be ranked directly.

The [whisper.cpp model repository](https://huggingface.co/ggerganov/whisper.cpp) also supplies Tiny Q5 models: 32.17 MB English and 32.15 MB multilingual. Both are now available in the catalog with pinned revision/hash/size. Smaller weights do not imply native streaming or better accuracy; Zipformer remains the bundled default for incremental recognition, while Tiny variants are alternatives for windowed recognition.

Tiny English Q5 was subsequently downloaded and SHA-256 verified, then tested on the same fixture with Vulkan: warm range 0.28–0.34 s, expected fixture text. The native benchmark process used about 148 MB resident RAM and recorded 0 CPU seconds during a two-second idle check. This is the benchmark process, not a full-app RAM or battery measurement; GPU VRAM is not included.
