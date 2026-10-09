using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Wyspa.Core.Services;

// Isolated, pinned embeddable runtime. No pip, shell, administrator install or global PATH changes.
public sealed class ManagedSpeechRuntime(OptionalDependencyStore? downloads = null, string? directory = null)
{
    public sealed record RuntimeFile(string Name, long Bytes, string Hash, string Url, bool Gpu = false);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static ManagedSpeechRuntime Default { get; } = new();
    public static IReadOnlyList<RuntimeFile> Files { get; } = LoadFiles();
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wyspa", "Dependencies", "speech-worker-v1");
    private static IReadOnlyList<RuntimeFile> LoadFiles()
    {
        using var input = typeof(ManagedSpeechRuntime).Assembly.GetManifestResourceStream("Wyspa.LocalWorker.Runtime")!;
        return JsonSerializer.Deserialize<List<RuntimeFile>>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private static Stream Script() => typeof(ManagedSpeechRuntime).Assembly.GetManifestResourceStream("Wyspa.LocalWorker.Script")!;
    public async Task<(string Python, string Script, string[] CudaPaths)> EnsureAsync(bool gpu, IProgress<string>? progress, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The managed local speech runtime requires Windows x64.");
        await Gate.WaitAsync(token);
        try
        {
            var store = downloads ?? OptionalDependencyStore.Default;
            var cpu = Path.Combine(DirectoryPath, "cpu");
            await PrepareDirectoryAsync(cpu, Files.Where(f => !f.Gpu).ToArray(), store, progress, token);
            var cuda = Array.Empty<string>();
            if (gpu)
            {
                // Reuse a complete local CUDA installation when its libraries are visible.
                cuda = FindCudaPaths();
                if (cuda.Length == 0)
                {
                    var gpuDirectory = Path.Combine(DirectoryPath, "cuda");
                    await PrepareDirectoryAsync(gpuDirectory, Files.Where(f => f.Gpu).ToArray(), store, progress, token);
                    cuda = Directory.GetDirectories(gpuDirectory, "bin", SearchOption.AllDirectories);
                }
            }
            var script = Path.Combine(cpu, "wyspa_worker.py");
            // Script is application-owned and embedded, rather than fetched from a model repository.
            using var source = Script(); await using (var output = File.Create(script)) await source.CopyToAsync(output, token);
            return (Path.Combine(cpu, "python.exe"), script, cuda);
        }
        finally { Gate.Release(); }
    }
    private static string[] FindCudaPaths()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(Directory.Exists).ToArray();
        return paths.Any(p => File.Exists(Path.Combine(p, "cublas64_12.dll"))) && paths.Any(p => File.Exists(Path.Combine(p, "cudnn64_9.dll"))) ? paths : [];
    }
    private static async Task PrepareDirectoryAsync(string destination, IReadOnlyList<RuntimeFile> files, OptionalDependencyStore store, IProgress<string>? progress, CancellationToken token)
    {
        if (await VerifyDirectoryAsync(destination, token)) { DiscardArchives(files, store); return; }
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                var dependency = new OptionalDependency("speech-worker-v1", "Local speech runtime · " + file.Name, file.Name, file.Bytes, file.Hash, file.Url, file.Bytes, file.Hash);
                var archive = await store.EnsureAsync(dependency, null, progress, token);
                var root = file.Name.EndsWith(".whl", StringComparison.Ordinal) ? Path.Combine(staging, "Lib", "site-packages") : staging;
                using var zip = ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/')) continue;
                    var relative = entry.FullName;
                    var data = relative.IndexOf(".data/", StringComparison.Ordinal);
                    if (data >= 0 && (relative.Contains(".data/purelib/") || relative.Contains(".data/platlib/"))) relative = relative[(data + 6)..].Split('/', 2)[1];
                    var path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid runtime archive path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var input = entry.Open(); await using var output = File.Create(path); await input.CopyToAsync(output, token);
                }
            }
            if (File.Exists(Path.Combine(staging, "python.exe")))
                await File.WriteAllTextAsync(Path.Combine(staging, "python312._pth"), "python312.zip\n.\nLib/site-packages\nimport site\n", token);
            var checks = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                await using var input = File.OpenRead(path); checks[Path.GetRelativePath(staging, path)] = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            }
            await File.WriteAllTextAsync(Path.Combine(staging, "verified-files.json"), JsonSerializer.Serialize(checks), token);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(staging, destination);
            DiscardArchives(files, store);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static void DiscardArchives(IReadOnlyList<RuntimeFile> files, OptionalDependencyStore store)
    {
        // Verified extracted libraries are the cache; avoid keeping another gigabyte of archives.
        foreach (var file in files)
        {
            try { File.Delete(Path.Combine(store.DirectoryPath, "speech-worker-v1", file.Name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private static async Task<bool> VerifyDirectoryAsync(string directory, CancellationToken token)
    {
        var manifest = Path.Combine(directory, "verified-files.json");
        if (!File.Exists(manifest)) return false;
        try
        {
            var checks = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(manifest, token))!;
            foreach (var (relative, expected) in checks)
            {
                var path = Path.GetFullPath(Path.Combine(directory, relative));
                if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
                await using var input = File.OpenRead(path);
                if (Convert.ToHexString(await SHA256.HashDataAsync(input, token)) != expected) return false;
            }
            return checks.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return false; }
    }
}
