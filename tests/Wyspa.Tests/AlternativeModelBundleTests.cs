using System.Net;
using System.Security.Cryptography;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class AlternativeModelBundleTests
{
    [Fact]
    public async Task MultiFileModelActivatesOnlyWhenEveryFileIsVerified_AndRemovesCleanly()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var encoder = new byte[] { 1, 2, 3 }; var tokens = new byte[] { 4, 5 };
        string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
        var bundle = new LocalModelBundle("Parakeet", "English", "test", "MIT", "https://example.test", "https://example.test/revision", [new("encoder.onnx", 3, Hash(encoder), "https://example.test/encoder"), new("tokens.txt", 2, Hash(tokens), "https://example.test/tokens")]);
        var model = new LocalModel("test.en-int8", "Test", 5, "", Bundle: bundle);
        using var handler = new Handler(encoder, tokens); using var http = new HttpClient(handler); var store = new LocalModelStore(http, directory, [model]);
        try
        {
            await store.DownloadAsync(model.Id, new Progress<double>(), default);
            Assert.True(store.IsInstalled(model.Id)); await store.VerifyAsync(model.Id, default);
            Assert.True(File.Exists(Path.Combine(store.ModelPath(model.Id), "NOTICE.txt")));
            store.Remove(model.Id); Assert.False(store.IsInstalled(model.Id));
            handler.Bad = true;
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(model.Id, new Progress<double>(), default));
            Assert.False(store.IsInstalled(model.Id)); Assert.Empty(Directory.GetDirectories(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void NewCatalogOptionsHaveWholeBundleSizesBelowCapAndExplicitCpuEngine()
    {
        Assert.Equal(5, AlternativeSpeechModels.Catalog.Count);
        Assert.All(AlternativeSpeechModels.Catalog, model =>
        {
            Assert.InRange(model.Bytes, 1, 1_500_000_000); Assert.True(model.IsFolder);
            Assert.Equal(model.Bytes, model.Bundle!.Files.Sum(f => f.Bytes)); Assert.Contains("CPU", model.Details);
            Assert.All(model.Bundle.Files, file => Assert.Equal(64, file.Hash.Length));
        });
        Assert.False(AlternativeSpeechModels.Catalog.Single(m => m.Id == "parakeet-v3-int8").EnglishOnly);
        Assert.True(AlternativeSpeechModels.Catalog.Single(m => m.Id == "moonshine-base.en-int8").EnglishOnly);
        Assert.True(AlternativeSpeechModels.Catalog.Single(m => m.Id == "zipformer-streaming.en-int8").Continuous);
    }
    private sealed class Handler(byte[] encoder, byte[] tokens) : HttpMessageHandler
    {
        public bool Bad;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/encoder" ? encoder : Bad ? [0, 0] : tokens) });
    }
}
