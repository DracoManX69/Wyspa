namespace Wyspa.Core.Services;

public sealed record LocalModelFile(string Name, long Bytes, string Hash, string Url);
public sealed record LocalModelBundle(string Engine, string Languages, string Publisher, string License, string Origin, string Source, IReadOnlyList<LocalModelFile> Files);

public static class AlternativeSpeechModels
{
    public static IReadOnlyList<LocalModel> Catalog { get; } =
    [
        new("parakeet-v3-int8", "NVIDIA Parakeet TDT 0.6B v3 INT8", 670478772, "", "CPU ONNX engine. Vocabulary prompting is not supported. Bounded-window transcription; punctuation and performance depend on the model.", "INT8", new("Parakeet", "25 European languages", "NVIDIA", "CC BY 4.0", "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/tree/2bda32ec70b097a55adaa07d9a7173915b43cc78",
        [
            new("encoder.int8.onnx", 652184281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/2bda32ec70b097a55adaa07d9a7173915b43cc78/encoder.int8.onnx"),
            new("decoder.int8.onnx", 11845275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/2bda32ec70b097a55adaa07d9a7173915b43cc78/decoder.int8.onnx"),
            new("joiner.int8.onnx", 6355277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/2bda32ec70b097a55adaa07d9a7173915b43cc78/joiner.int8.onnx"),
            new("tokens.txt", 93939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/2bda32ec70b097a55adaa07d9a7173915b43cc78/tokens.txt"),
        ])),
        new("parakeet-v2.en-int8", "NVIDIA Parakeet TDT 0.6B v2 English INT8", 661190513, "", "CPU ONNX engine. Vocabulary prompting is not supported. Bounded-window transcription; punctuation and performance depend on the model.", "INT8", new("Parakeet", "English", "NVIDIA", "CC BY 4.0", "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/tree/1ab9323565ddb038682214b292f588070a538ce2",
        [
            new("encoder.int8.onnx", 652184296, "a32b12d17bbbc309d0686fbbcc2987b5e9b8333a7da83fa6b089f0a2acd651ab", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/1ab9323565ddb038682214b292f588070a538ce2/encoder.int8.onnx"),
            new("decoder.int8.onnx", 7257753, "b6bb64963457237b900e496ee9994b59294526439fbcc1fecf705b31a15c6b4e", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/1ab9323565ddb038682214b292f588070a538ce2/decoder.int8.onnx"),
            new("joiner.int8.onnx", 1739080, "7946164367946e7f9f29a122407c3252b680dbae9a51343eb2488d057c3c43d2", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/1ab9323565ddb038682214b292f588070a538ce2/joiner.int8.onnx"),
            new("tokens.txt", 9384, "ec182b70dd42113aff6c5372c75cac58c952443eb22322f57bbd7f53977d497d", "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/1ab9323565ddb038682214b292f588070a538ce2/tokens.txt"),
        ])),
        new("moonshine-tiny.en-int8", "Moonshine Tiny English INT8", 123967539, "", "CPU ONNX engine. Vocabulary prompting is not supported. Bounded-window transcription; punctuation and performance depend on the model.", "INT8", new("Moonshine", "English", "Useful Sensors", "MIT", "https://huggingface.co/UsefulSensors/moonshine-tiny", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/tree/bf2b762c076d8ea61e2af0b3851c9564fb77552e",
        [
            new("preprocess.onnx", 6800738, "f33addce61a143460fe753b5ee5b7db255e5140b5b779c065b94f6c83ff0bf4e", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/resolve/bf2b762c076d8ea61e2af0b3851c9564fb77552e/preprocess.onnx"),
            new("encode.int8.onnx", 18249187, "8774dfba578de027ec6595c2c654a0836434489bc963a0db124a7f181f571acb", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/resolve/bf2b762c076d8ea61e2af0b3851c9564fb77552e/encode.int8.onnx"),
            new("uncached_decode.int8.onnx", 53216096, "216737000dd5881a17aa043f6bbd286add33e4c3b0ae257153e2ec15438bdc41", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/resolve/bf2b762c076d8ea61e2af0b3851c9564fb77552e/uncached_decode.int8.onnx"),
            new("cached_decode.int8.onnx", 45264830, "2aff28bba6a03d8dcf5c9feac45462629bae37317442299f28115ad09da773f6", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/resolve/bf2b762c076d8ea61e2af0b3851c9564fb77552e/cached_decode.int8.onnx"),
            new("tokens.txt", 436688, "1165c2aeb9f72f457a83be2d459a09054f27490acd9b41bd43794dfd25e296ea", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-tiny-en-int8/resolve/bf2b762c076d8ea61e2af0b3851c9564fb77552e/tokens.txt"),
        ])),
        new("moonshine-base.en-int8", "Moonshine Base English INT8", 286929760, "", "CPU ONNX engine. Vocabulary prompting is not supported. Bounded-window transcription; punctuation and performance depend on the model.", "INT8", new("Moonshine", "English", "Useful Sensors", "MIT", "https://huggingface.co/UsefulSensors/moonshine-base", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/tree/052b0798ad1bf046a140fdd4efcd9426530fa3f5",
        [
            new("preprocess.onnx", 14077290, "ffa630d395c5ccf76f5d4954be5b882df76aaf6491519ec01fd82ea7a3819fb2", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/resolve/052b0798ad1bf046a140fdd4efcd9426530fa3f5/preprocess.onnx"),
            new("encode.int8.onnx", 50311494, "7e38770f776f2e5583a53b052936005df2ba5c833d7e09c2a5fd796b94bf73e2", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/resolve/052b0798ad1bf046a140fdd4efcd9426530fa3f5/encode.int8.onnx"),
            new("uncached_decode.int8.onnx", 122120451, "c01f4b35093bcac20d352d23a75a539e772964579f9d024a90e5e6f09cae9987", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/resolve/052b0798ad1bf046a140fdd4efcd9426530fa3f5/uncached_decode.int8.onnx"),
            new("cached_decode.int8.onnx", 99983837, "2db74e51cedf64a8b1be3c8192e0bb5e4923af0e90bd9e87f8e8771873f8ea03", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/resolve/052b0798ad1bf046a140fdd4efcd9426530fa3f5/cached_decode.int8.onnx"),
            new("tokens.txt", 436688, "1165c2aeb9f72f457a83be2d459a09054f27490acd9b41bd43794dfd25e296ea", "https://huggingface.co/csukuangfj/sherpa-onnx-moonshine-base-en-int8/resolve/052b0798ad1bf046a140fdd4efcd9426530fa3f5/tokens.txt"),
        ])),
        new("zipformer-streaming.en-int8", "Zipformer English streaming INT8", 73440167, "", "CPU ONNX engine. Vocabulary prompting is not supported. Continuous streaming; no automatic punctuation.", "INT8", new("Zipformer", "English", "Next-gen Kaldi / icefall", "Apache 2.0", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/tree/672fbf1b30579d6585301139bb363f42a0ad4a24",
        [
            new("encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx", 71083163, "563fde436d16cf7607cf408cd6b30909819d03162652ef389c2450ced3f45ac1", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/672fbf1b30579d6585301139bb363f42a0ad4a24/encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx"),
            new("decoder-epoch-99-avg-1-chunk-16-left-128.onnx", 2092621, "7bf787f90b194b307e5a4ad6a34fadb4e748304c35f78a8d66358a05b13ee6ef", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/672fbf1b30579d6585301139bb363f42a0ad4a24/decoder-epoch-99-avg-1-chunk-16-left-128.onnx"),
            new("joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx", 259335, "d944208d660d67c8d72cd2acaeac971fa5ceb8c80e76c1968148846fedd6e297", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/672fbf1b30579d6585301139bb363f42a0ad4a24/joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx"),
            new("tokens.txt", 5048, "49e3c2646595fd907228b3c6787069658f67b17377c60aeb8619c4551b2316fb", "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/672fbf1b30579d6585301139bb363f42a0ad4a24/tokens.txt"),
        ])),
    ];
}
