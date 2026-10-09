namespace Wyspa.Core.Services;

public static class ExtraSpeechModels
{
    public static IReadOnlyList<LocalModel> Catalog { get; } =
    [
        new("parakeet-redux-onnx", "Moondream Parakeet Redux ONNX", 417712821, "", "CPU ONNX export of the ternary Redux model. Shared managed runtime downloads once (91.5 MB); source Photon speed numbers do not describe this export. Language is detected; prompts are not supported.", "4-bit blocks", new("Redux", "25 European languages", "Moondream / NVIDIA", "CC BY 4.0", "https://huggingface.co/moondream/parakeet-redux", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/tree/1c285ba78d6f1880803f65355133a5ad3559a407",
        [
            new("preprocessor.onnx", 1224294, "3de5742f5780fc8615c1d883a2a60e28621ff13d2e20d24d76e94b9e4168445f", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/resolve/1c285ba78d6f1880803f65355133a5ad3559a407/preprocessor.onnx"),
            new("encoder-model.onnx", 343841943, "ade2c65c188776b3662db620a553ab82ac7e9ca11a7be6b696bcc5190f75382e", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/resolve/1c285ba78d6f1880803f65355133a5ad3559a407/encoder-model.onnx"),
            new("decoder_joint-model.onnx", 72552270, "97ae0cb049f32a5d7a6572e91020b8d87d66baffa0ff9de2cf6de4c0960e92f8", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/resolve/1c285ba78d6f1880803f65355133a5ad3559a407/decoder_joint-model.onnx"),
            new("vocab.txt", 93939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/resolve/1c285ba78d6f1880803f65355133a5ad3559a407/vocab.txt"),
            new("config.json", 375, "d4d3cfc0bc8a50007be6eee4df58c026850132f23c39581a01685dcb4351a430", "https://huggingface.co/eschmidbauer/parakeet-redux-onnx/resolve/1c285ba78d6f1880803f65355133a5ad3559a407/config.json"),
        ])),
        new("sensevoice-small-int8", "SenseVoice Small INT8", 237431441, "", "CPU ONNX engine. Recognition with inverse text normalization; vocabulary prompts are not supported.", "INT8", new("SenseVoice", "Mandarin, Cantonese, English, Japanese, Korean", "FunAudioLLM / Alibaba", "Apache 2.0", "https://huggingface.co/FunAudioLLM/SenseVoiceSmall", "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09/tree/355f4d4884d8afd08aef04b9007a8556d7b463b2",
        [
            new("model.int8.onnx", 237115547, "12ca1a2ae7ecf3e0019ef2822307ee0b5cadc9196569e379b4c4026f8205276d", "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09/resolve/355f4d4884d8afd08aef04b9007a8556d7b463b2/model.int8.onnx"),
            new("tokens.txt", 315894, "f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc", "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09/resolve/355f4d4884d8afd08aef04b9007a8556d7b463b2/tokens.txt"),
        ])),
        new("faster-whisper-tiny.en", "Faster-Whisper Tiny English", 78090594, "", "CTranslate2 CPU INT8 or NVIDIA CUDA INT8/FP16 engine. Shared managed runtime downloads once (91.5 MB). Optional CUDA libraries download on first GPU use (1076.8 MB) if not already available; these are shared by all Faster-Whisper models.", "INT8 at runtime", new("FasterWhisper", "English", "OpenAI / SYSTRAN", "MIT", "https://huggingface.co/Systran/faster-whisper-tiny.en", "https://huggingface.co/Systran/faster-whisper-tiny.en/tree/0d3d19a32d3338f10357c0889762bd8d64bbdeba",
        [
            new("model.bin", 75537502, "1a5afae06a4db91c975c9a9d78be5cc110ee4ea022ad57d55492e4550e936b2a", "https://huggingface.co/Systran/faster-whisper-tiny.en/resolve/0d3d19a32d3338f10357c0889762bd8d64bbdeba/model.bin"),
            new("config.json", 2317, "14b1b421a90349bc551b881461426b561a874049cb9e4c4864f2ca384f6a7cc5", "https://huggingface.co/Systran/faster-whisper-tiny.en/resolve/0d3d19a32d3338f10357c0889762bd8d64bbdeba/config.json"),
            new("tokenizer.json", 2128466, "929c5252409436dce1b38a75d1abbcb5e132d170d8e324e4e04ed915fa2d22df", "https://huggingface.co/Systran/faster-whisper-tiny.en/resolve/0d3d19a32d3338f10357c0889762bd8d64bbdeba/tokenizer.json"),
            new("vocabulary.txt", 422309, "ff77588746d3a2595d32ab5b69ffd7b95ce2441ac57533cb66fc3eb575a115cf", "https://huggingface.co/Systran/faster-whisper-tiny.en/resolve/0d3d19a32d3338f10357c0889762bd8d64bbdeba/vocabulary.txt"),
        ])),
        new("faster-whisper-base.en", "Faster-Whisper Base English", 147769510, "", "CTranslate2 CPU INT8 or NVIDIA CUDA INT8/FP16 engine. Shared managed runtime downloads once (91.5 MB). Optional CUDA libraries download on first GPU use (1076.8 MB) if not already available; these are shared by all Faster-Whisper models.", "INT8 at runtime", new("FasterWhisper", "English", "OpenAI / SYSTRAN", "MIT", "https://huggingface.co/Systran/faster-whisper-base.en", "https://huggingface.co/Systran/faster-whisper-base.en/tree/3d3d5dee26484f91867d81cb899cfcf72b96be6c",
        [
            new("model.bin", 145216508, "2a166925539a16005f14ff328359f9b9adb9dc4fb631bb3b227526862e93e2ef", "https://huggingface.co/Systran/faster-whisper-base.en/resolve/3d3d5dee26484f91867d81cb899cfcf72b96be6c/model.bin"),
            new("config.json", 2227, "f3bc3821e9fc76a27bae538e11ae5b677dcdd352b4600429ce7951d398569aeb", "https://huggingface.co/Systran/faster-whisper-base.en/resolve/3d3d5dee26484f91867d81cb899cfcf72b96be6c/config.json"),
            new("tokenizer.json", 2128466, "929c5252409436dce1b38a75d1abbcb5e132d170d8e324e4e04ed915fa2d22df", "https://huggingface.co/Systran/faster-whisper-base.en/resolve/3d3d5dee26484f91867d81cb899cfcf72b96be6c/tokenizer.json"),
            new("vocabulary.txt", 422309, "ff77588746d3a2595d32ab5b69ffd7b95ce2441ac57533cb66fc3eb575a115cf", "https://huggingface.co/Systran/faster-whisper-base.en/resolve/3d3d5dee26484f91867d81cb899cfcf72b96be6c/vocabulary.txt"),
        ])),
    ];
}
