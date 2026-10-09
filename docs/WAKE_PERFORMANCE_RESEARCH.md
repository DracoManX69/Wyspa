# Wake detection refinement · 8 October 2026

Wyspa already separates wake detection from dictation: a small local keyword spotter listens continuously, an independent local word verifier confirms candidates, then the selected local/Groq transcription provider handles dictation. Speech recognition is not run continuously through a large transcription model merely to look for a wake phrase.

## Research and model choice

| Engine | Findings relevant to Wyspa | Decision |
| --- | --- | --- |
| sherpa-onnx English GigaSpeech Zipformer 3.3M INT8 | Current approximately 6.3 MB bundle supports arbitrary English BPE keyword spellings and learned variants. All 18 positive and 30 negative synthesized fixtures were classified correctly by the keyword stage at the comparison threshold. | Retain the current wake model. |
| sherpa-onnx Chinese/English Zipformer 3M, December 2025, chunk 8 INT8 | Official docs state 160 ms architectural latency versus 320 ms for chunk 16. English words need pronunciation lexicon/phone tokenization. At the same test threshold it missed two of 18 positives and used more CPU processing time on this host. Custom spellings such as Wyspa/whisba also need a pronunciation path beyond its lexicon. | Research candidate, not a justified default replacement from these results. |
| openWakeWord | Open source code, Windows ONNX support and custom speaker/environment verifiers. Included pretrained weights use CC BY-NC-SA 4.0; custom target phrases require model training rather than simply inserting a keyword into an open vocabulary decoder. | Useful architecture reference; not a drop-in permissive custom Hey Whisper bundle. |
| microWakeWord | Very small streaming models suitable for microcontrollers. Custom training requires representative positive/ambient-negative data and experimentation; source describes it as advanced work. | Promising for a separately trained fixed phrase model; a few setup readings are not equivalent to that training. |
| Picovoice Porcupine | Wake-specific native inference and custom phrase models, but its current model API requires an AccessKey. | Does not fit the existing account-free managed setup requirement as the default. |
| Bundled Zipformer 20M ASR as verifier | Faster than Whisper on some short clips, but transcribed the opening as `Y WHISPER` in all 18 positive test clips. | Retain independent Whisper verification rather than trading word accuracy for speed. |

Primary references: [sherpa keyword decoder](https://k2-fsa.github.io/sherpa/onnx/kws/index.html), [new 3M model and latency/lexicon requirements](https://k2-fsa.github.io/sherpa/onnx/kws/pretrained_models/index.html), [openWakeWord platform/licensing](https://github.com/dscripka/openWakeWord), [custom verifier guidance](https://github.com/dscripka/openWakeWord/blob/main/docs/custom_verifier_models.md), [microWakeWord training and ambient evaluation](https://github.com/OHF-Voice/micro-wake-word), [Porcupine model authentication](https://picovoice.ai/docs/model-api/porcupine/).

## Controlled comparisons

48 Windows SAPI fixtures use two English voices, three rates and eight sentences: 18 positive phrase/request readings and 30 ordinary/near/isolated-word negatives. These are generated fixtures, not independent people or real microphone tests. All processing is local; no audio is uploaded. Model archive and fixture artifacts stay outside the installer.

At strictness .45 (acoustic trigger threshold .46), the current keyword model scored 18 true positives, zero misses and zero false positives. The newer model scored 16, two and zero. Positive keyword-stage median CPU wall processing was approximately 26.8 ms for the current model and 36.0 ms for the newer model. These accelerated processing times exclude recording time and are distinct from architectural lookahead and perceived wake latency.

Original Tiny English Q5 verification accepted 17 of the 18 positive candidate clips and rejected all 30 negatives. The failure was a partial request boundary decoded as `whispeart`; the small model could repeat that fragment. Blindly shortening the clip was faster but initially lost six positives, so that version was rejected. The final approach uses keyword token timing with generous final-token room, limits short-phrase decoding, retains a full-window word check and allows one short additional-audio retry if neither word check confirms the phrase. No forced wake transcript/grammar/prompt is used. The metadata wrapper is pinned to the documented sherpa 1.13.8 C result layout and owns its native model through SafeHandle.

Final corpus, reduced-volume/noise and native UI results are recorded in V095_VALIDATION.md. A 48-clip pass is not a 99.99% estimate or proof of Alexa-equivalent reliability. Actual accents, microphones, reverberation, media echo and unattended false-trigger hours remain independent acceptance work.
