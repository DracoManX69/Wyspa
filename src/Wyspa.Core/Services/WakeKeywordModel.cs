namespace Wyspa.Core.Services;

public static class WakeKeywordModel
{
    // Mirror files were byte/hash compared against the official k2-fsa kws-models
    // archive (SHA256 f170013b4716e41b62b9bfd809687c207cef798ef9bc6534d524e17af9b6561a).
    public const string Repository = "mobilebytesensei/sherpa-onnx-kws-zipformer-gigaspeech-3.3M";
    public const string Revision = "e8ca74a794ce66e38a100e7c60c0214189ff4100";
    public static IReadOnlyList<OptionalDependency> Files { get; } =
    [
        File("encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx", 4807159, "1e721676515bcd42a186979733981213c66c80db680e1cc582dfedf3be76e678"),
        File("decoder-epoch-12-avg-2-chunk-16-left-64.onnx", 1063189, "f61ebd3eed3773a44d088d53dfae92dbb6aec4839f4dcaee2d402414741663a3"),
        File("joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx", 163380, "eae9da0c7e1e6c6a3f4cc42d167899c388f6c6701b94cb96320e4f55df79624c"),
        File("bpe.model", 244837, "c8a2a0129c4ab8e463164c142f82d25649661b122c8cd0b7aab5c9e80b90ad24"),
        File("tokens.txt", 5006, "fd2ded4050a55d2b1578870ba8697d02371980217806b7558bd0a5cc60f3ba53")
    ];
    private static OptionalDependency File(string name, long bytes, string hash) => new("wake-keyword-en-v1", "Wake phrase detector", name, bytes, hash,
        $"https://huggingface.co/{Repository}/resolve/{Revision}/{name}", bytes, hash);
}
