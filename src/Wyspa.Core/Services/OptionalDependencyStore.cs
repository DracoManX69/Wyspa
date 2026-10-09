using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Wyspa.Core.Services;

public sealed record OptionalDependency(string Id, string Name, string FileName, long Bytes, string Sha256,
    string Url, long DownloadBytes, string DownloadSha256, string? ZipEntry = null);

// Optional feature files live outside the install directory and survive upgrades.
// Each download and extracted file is size/hash checked before atomic activation.
public sealed class OptionalDependencyStore(HttpClient http, string? directory = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(15) };
    public static OptionalDependencyStore Default { get; } = new(SharedHttp);
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wyspa", "Dependencies");
    public static OptionalDependency Deno { get; } = new("deno-2.9.7", "YouTube support", "deno.exe", 97462048,
        "e020f3e232bd16e33768dee528e5983349c962952051ced0a5d58ad42f5d9b33",
        "https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip", 42630221,
        "a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238", "deno.exe");
    public static OptionalDependency Segmentation { get; } = new("speaker-segmentation-3.0", "Speaker detection model", "segmentation.onnx", 5992913,
        "220ad67ca923bef2fa91f2390c786097bf305bceb5e261d4af67b38e938e1079",
        "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/9403a6902bb58e3d5ae8c7e77c3422de279db2e0/model.onnx", 5992913,
        "220ad67ca923bef2fa91f2390c786097bf305bceb5e261d4af67b38e938e1079");
    public static OptionalDependency Embedding { get; } = new("speaker-embedding-campplus-en", "Speaker identification model", "embedding.onnx", 29596978,
        "357a834f702b80161e5b981182c038e18553c1f2ca752ed6cec2052365d4129b",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_campplus_sv_en_voxceleb_16k.onnx", 29596978,
        "357a834f702b80161e5b981182c038e18553c1f2ca752ed6cec2052365d4129b");

    public async Task<string> EnsureAsync(OptionalDependency dependency, string? bundledFile, IProgress<string>? progress, CancellationToken token)
    {
        if (Path.GetFileName(dependency.FileName) != dependency.FileName ||
            !System.Text.RegularExpressions.Regex.IsMatch(dependency.Id, @"\A[A-Za-z0-9._-]+\z")) throw new ArgumentException("Invalid dependency path.");
        var destination = Path.Combine(DirectoryPath, dependency.Id, dependency.FileName);
        var gate = Gates.GetOrAdd(Path.GetFullPath(destination), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            if (await MatchesAsync(destination, dependency.Bytes, dependency.Sha256, token)) return destination;
            // Older installations already have these exact files. Keep them usable offline.
            if (bundledFile is not null && await MatchesAsync(bundledFile, dependency.Bytes, dependency.Sha256, token)) return bundledFile;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
            var extracted = temporary + ".extracted";
            try
            {
                progress?.Report($"Downloading {dependency.Name} for first use… Internet is needed once.");
                using var response = await http.GetAsync(dependency.Url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using (var source = await response.Content.ReadAsStreamAsync(token))
                await using (var output = File.Create(temporary))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[131072]; long total = 0; int read; var lastPercent = -1;
                    while ((read = await source.ReadAsync(buffer, token)) > 0)
                    {
                        total += read;
                        if (total > dependency.DownloadBytes) throw new InvalidDataException("Unexpected download size.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        var percent = (int)(total * 100 / dependency.DownloadBytes);
                        if (percent != lastPercent) { progress?.Report($"Downloading {dependency.Name}… {percent}%"); lastPercent = percent; }
                    }
                    if (total != dependency.DownloadBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(dependency.DownloadSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The dependency download did not match its verified version.");
                }
                var ready = temporary;
                if (dependency.ZipEntry is not null)
                {
                    using var archive = ZipFile.OpenRead(temporary);
                    var entry = archive.GetEntry(dependency.ZipEntry) ?? throw new InvalidDataException("The download is missing the required file.");
                    if (entry.Length != dependency.Bytes) throw new InvalidDataException("Unexpected extracted size.");
                    await using var source = entry.Open();
                    await using (var output = File.Create(extracted)) await source.CopyToAsync(output, token);
                    ready = extracted;
                }
                if (!await MatchesAsync(ready, dependency.Bytes, dependency.Sha256, token)) throw new InvalidDataException("Dependency verification failed.");
                token.ThrowIfCancellationRequested();
                File.Move(ready, destination, true);
                progress?.Report($"{dependency.Name} is ready. Future use works from the local cache.");
                return destination;
            }
            finally { File.Delete(temporary); File.Delete(extracted); }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        { throw new InvalidOperationException($"Could not prepare {dependency.Name}. Check your connection and try again. " + ex.Message, ex); }
        finally { gate.Release(); }
    }
    private static async Task<bool> MatchesAsync(string path, long bytes, string sha256, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != bytes) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
