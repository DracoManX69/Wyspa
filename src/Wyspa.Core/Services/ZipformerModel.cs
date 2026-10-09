namespace Wyspa.Core.Services;

public static class ZipformerModel
{
    public const string Id = "zipformer.en-20m";
    public const string Repository = "csukuangfj/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17";
    public const string Revision = "d42f2d9f7ca24806fb667456a18a9f1b60f70d16";
    public static readonly (string Name, long Bytes, string Hash)[] Files =
    [
        ("encoder-epoch-99-avg-1.int8.onnx", 42845182, "3810755ce7c3ab26b42a8bcf39d191308fa27fb0f53358823ba46141d03b7eb3"),
        ("decoder-epoch-99-avg-1.onnx", 2092272, "45a7f940ecfb53d89fa270ad11b88b961e53a317203eb24b1c8e95ed208b0f30"),
        ("joiner-epoch-99-avg-1.int8.onnx", 259572, "e085d73b593cf9b0707f370dbd656d58327d3fe36d80d849202ef81df02cb01e"),
        ("tokens.txt", 5048, "49e3c2646595fd907228b3c6787069658f67b17377c60aeb8619c4551b2316fb")
    ];
    public static string Url(string file) => $"https://huggingface.co/{Repository}/resolve/{Revision}/{file}";
}
