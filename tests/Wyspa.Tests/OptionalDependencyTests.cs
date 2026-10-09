using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class OptionalDependencyTests
{
    [Fact]
    public async Task DownloadsOnce_VerifiesAndReusesCacheAcrossInstances()
    {
        using var setup = new Setup("verified model"u8.ToArray());
        var a = setup.Store.EnsureAsync(setup.Dependency, null, null, default);
        var b = new OptionalDependencyStore(setup.Http, setup.Directory).EnsureAsync(setup.Dependency, null, null, default);
        var paths = await Task.WhenAll(a, b);
        Assert.Equal(paths[0], paths[1]); Assert.Equal(1, setup.Handler.Requests);
        Assert.Equal(setup.Data, await File.ReadAllBytesAsync(paths[0]));
    }
    [Fact]
    public async Task ExistingBundledFileWorksOffline_AndDoesNotDownload()
    {
        using var setup = new Setup("existing model"u8.ToArray());
        var bundled = Path.Combine(setup.Directory, "bundled.onnx"); await File.WriteAllBytesAsync(bundled, setup.Data);
        Assert.Equal(bundled, await setup.Store.EnsureAsync(setup.Dependency, bundled, null, default));
        Assert.Equal(0, setup.Handler.Requests);
    }
    [Fact]
    public async Task CorruptCacheIsReplacedOnlyAfterSuccessfulVerification()
    {
        using var setup = new Setup("good"u8.ToArray());
        var path = await setup.Store.EnsureAsync(setup.Dependency, null, null, default);
        await File.WriteAllTextAsync(path, "xxxx");
        setup.Handler.Data = "bad!"u8.ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.EnsureAsync(setup.Dependency, null, null, default));
        Assert.Equal("xxxx", await File.ReadAllTextAsync(path));
        Assert.Empty(System.IO.Directory.GetFiles(setup.Directory, "*.partial*", SearchOption.AllDirectories));
        setup.Handler.Data = setup.Data;
        await setup.Store.EnsureAsync(setup.Dependency, null, null, default);
        Assert.Equal(setup.Data, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task ZipExtractionVerifiesExecutable_AndRemovesArchive()
    {
        using var setup = new Setup("program"u8.ToArray());
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            using var output = archive.CreateEntry("deno.exe").Open(); output.Write(setup.Data);
        }
        setup.Handler.Data = zip.ToArray();
        var dependency = setup.Dependency with { FileName = "deno.exe", ZipEntry = "deno.exe", DownloadBytes = setup.Handler.Data.Length, DownloadSha256 = Hash(setup.Handler.Data) };
        var path = await setup.Store.EnsureAsync(dependency, null, null, default);
        Assert.Equal(setup.Data, await File.ReadAllBytesAsync(path));
        Assert.Single(System.IO.Directory.GetFiles(setup.Directory, "*", SearchOption.AllDirectories));
        await setup.Store.EnsureAsync(dependency, null, null, default); Assert.Equal(1, setup.Handler.Requests);
    }
    [Fact]
    public async Task CancelledDownloadLeavesNoPartialFile_AndCanRetry()
    {
        using var setup = new Setup("model"u8.ToArray()); using var cancellation = new CancellationTokenSource();
        setup.Handler.CancelOnRead = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Store.EnsureAsync(setup.Dependency, null, null, cancellation.Token));
        Assert.Empty(System.IO.Directory.GetFiles(setup.Directory, "*", SearchOption.AllDirectories));
        setup.Handler.CancelOnRead = null;
        Assert.True(File.Exists(await setup.Store.EnsureAsync(setup.Dependency, null, null, default)));
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private sealed class Setup : IDisposable
    {
        public string Directory = Path.Combine(Path.GetTempPath(), "wyspa-dependency-test-" + Guid.NewGuid());
        public byte[] Data; public Handler Handler; public HttpClient Http; public OptionalDependencyStore Store; public OptionalDependency Dependency;
        public Setup(byte[] data)
        {
            Data = data; System.IO.Directory.CreateDirectory(Directory); Handler = new Handler { Data = data }; Http = new HttpClient(Handler);
            Store = new(Http, Directory); Dependency = new("model", "Test model", "model.onnx", data.Length, Hash(data), "https://example.test/model", data.Length, Hash(data));
        }
        public void Dispose() { Http.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public byte[] Data = []; public int Requests; public CancellationTokenSource? CancelOnRead;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CancelOnRead is null
                ? new ByteArrayContent(Data) : new StreamContent(new CancellingStream(Data, CancelOnRead)) });
        }
    }
    private sealed class CancellingStream(byte[] data, CancellationTokenSource cancellation) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { cancellation.Cancel(); return ValueTask.FromCanceled<int>(token); }
    }
}
