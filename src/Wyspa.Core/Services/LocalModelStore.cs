using System.Security.Cryptography;

namespace Wyspa.Core.Services;

public sealed record LocalModel(string Id, string Name, long Bytes, string Sha256, string Description = "", string Encoding = "F16", LocalModelBundle? Bundle = null)
{
    public bool EnglishOnly => Bundle is { } bundle ? bundle.Languages == "English" : Id.Contains(".en", StringComparison.Ordinal);
    public bool IsFolder => Id == ZipformerModel.Id || Bundle is not null;
    public bool SupportsVocabulary => Bundle is null && !Continuous || Bundle?.Engine == "FasterWhisper";
    public bool Continuous => Id == ZipformerModel.Id || Bundle?.Engine == "Zipformer";
    public string InfoUrl => Bundle?.Origin ?? (Id == ZipformerModel.Id ? $"https://huggingface.co/{ZipformerModel.Repository}" : "https://huggingface.co/openai/whisper-" + (Id.StartsWith("large-v3-turbo") ? "large-v3-turbo" : Id.StartsWith("large-v3") ? "large-v3" : Id.Split('-')[0]));
    public string Size => Bytes >= 1_000_000_000 ? $"{Bytes / 1_000_000_000d:0.00} GB" : $"{Bytes / 1_000_000d:0.0} MB";
    public string DisplayName => $"{Name} · {Size}";
    public string FileName => Bundle is not null ? string.Join(", ", Bundle.Files.Select(f => f.Name)) : Id == ZipformerModel.Id ? "Zipformer: encoder, decoder, joiner and tokens" : "ggml-" + Id + ".bin";
    public string SourceUrl => Bundle is not null ? Bundle.Source : Id == ZipformerModel.Id ? $"https://huggingface.co/{ZipformerModel.Repository}/tree/{ZipformerModel.Revision}" : $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{LocalModelStore.SourceRevision}/{FileName}";
    public string Details => Bundle is not null ? $"{Bundle.Publisher} · {Bundle.Languages} · {Bundle.License} · {Size} ({Bytes:N0} bytes). {Description} Download size is not RAM usage." : Id == ZipformerModel.Id ? $"Next-gen Kaldi / icefall · English only · INT8 encoder and joiner · {Size}. Small streaming model included with Wyspa; runs on CPU with up to two threads. No automatic punctuation. Whisper offers punctuation, multilingual support and optional GPU acceleration." : $"OpenAI {Name} · {(EnglishOnly ? "English only" : "Multilingual")} · {Encoding} · {Size} ({Bytes:N0} bytes). {Description} Download size is not RAM usage; larger models can use several GB of memory.";
}

public sealed class LocalModelStore(HttpClient http, string? directory = null, IReadOnlyList<LocalModel>? catalog = null)
{
    public const string SourceRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";
    public static IReadOnlyList<LocalModel> Catalog { get; } =
    [
        new(ZipformerModel.Id, "Zipformer English 20M · fast dictation", ZipformerModel.Files.Sum(f => f.Bytes), "", "Included with Wyspa.", "INT8"),
        new("tiny.en-q5_1", "Whisper Tiny English Q5", 32166155, "c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b", "Smallest English Whisper download; reduced weight precision.", "Q5_1"),
        new("tiny-q5_1", "Whisper Tiny Q5", 32152673, "818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7", "Smallest multilingual Whisper download; reduced weight precision.", "Q5_1"),
        new("tiny.en", "Whisper Tiny English", 77704715, "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f", "Fast English Whisper choice; lower accuracy than larger models.", "F16"),
        new("tiny", "Whisper Tiny", 77691713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", "Fast multilingual Whisper choice; lower accuracy than larger models.", "F16"),
        new("base.en", "Whisper Base English", 147964211, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002", "A starting point for English on modest CPUs.", "F16"),
        new("base", "Whisper Base", 147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", "A starting point for multilingual CPU use.", "F16"),
        new("small.en", "Whisper Small English", 487614201, "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d", "More capable English model; slower than Base.", "F16"),
        new("small", "Whisper Small", 487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "More capable multilingual model; slower than Base.", "F16"),
        new("small.en-q5_1", "Whisper Small English Q5", 190098681, "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30", "Compact English option; quantization trades some precision for size.", "Q5_1"),
        new("small-q5_1", "Whisper Small Q5", 190085487, "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb", "Compact multilingual option; benchmark it on your PC.", "Q5_1"),
        new("medium.en-q5_0", "Whisper Medium English Q5", 539225533, "76733e26ad8fe1c7a5bf7531a9d41917b2adc0f20f2e4f5531688a8c6cd88eb0", "Larger English model; higher CPU and RAM needs.", "Q5_0"),
        new("medium-q5_0", "Whisper Medium Q5", 539212467, "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f", "Larger multilingual model; higher CPU and RAM needs.", "Q5_0"),
        new("large-v3-turbo-q5_0", "Whisper Large v3 Turbo Q5", 574041195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", "Speed-oriented Large v3 variant with a compact decoder; a useful upgrade to test.", "Q5_0"),
        new("large-v3-turbo-q8_0", "Whisper Large v3 Turbo Q8", 874188075, "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1", "Higher precision than Q5 with a larger download.", "Q8_0"),
        new("large-v3-q5_0", "Whisper Large v3 Q5", 1081140203, "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1", "Accuracy-oriented option; expect slower CPU processing.", "Q5_0"),
        .. AlternativeSpeechModels.Catalog,
        .. ExtraSpeechModels.Catalog
    ];
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wyspa", "Models");
    public IReadOnlyList<LocalModel> Models => catalog ?? Catalog;
    private IReadOnlyList<LocalModel> Available => Models;
    public LocalModel Find(string id) => Available.FirstOrDefault(m => m.Id == id) ?? throw new InvalidOperationException("Choose a supported local model in Settings → Local models.");
    public string ModelPath(string id) { Find(id); return Path.Combine(DirectoryPath, Find(id).IsFolder ? id : "ggml-" + id + ".bin"); }
    public bool IsInstalled(string id) => Available.Any(m => m.Id == id) && (Find(id).IsFolder ? Files(id).All(f => File.Exists(Path.Combine(ModelPath(id), f.Name)) && new FileInfo(Path.Combine(ModelPath(id), f.Name)).Length == f.Bytes) : File.Exists(ModelPath(id)) && new FileInfo(ModelPath(id)).Length == Find(id).Bytes);
    public async Task VerifyAsync(string id, CancellationToken token)
    {
        if (!IsInstalled(id)) throw new InvalidOperationException("Download the selected model in Settings → Local models first.");
        if (Find(id).IsFolder)
        {
            foreach (var item in Files(id))
            {
                await using var part = File.OpenRead(Path.Combine(ModelPath(id), item.Name));
                if (!Convert.ToHexString(await SHA256.HashDataAsync(part, token)).Equals(item.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The bundled speech model is damaged. Remove and download it again.");
            }
            return;
        }
        await using var file = File.OpenRead(ModelPath(id));
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(Find(id).Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The local model is damaged. Remove it and download it again in Settings → Local models.");
    }
    public async Task DownloadAsync(string id, IProgress<double> progress, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { if (Find(id).IsFolder) await InstallBundleAsync(id, null, progress, token); else await DownloadAttemptAsync(id, progress, token); return; }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested &&
                (ex is HttpRequestException { StatusCode: null or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.GatewayTimeout } || ex is IOException))
            { progress.Report(0); await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token); }
        }
    }
    private async Task DownloadAttemptAsync(string id, IProgress<double> progress, CancellationToken token)
    {
        var model = Find(id);
        Directory.CreateDirectory(DirectoryPath);
        var temp = ModelPath(id) + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await http.GetAsync(model.SourceUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[131072]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > model.Bytes) throw new InvalidDataException("Model download has an unexpected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress.Report(total * 100d / model.Bytes);
                }
                if (total != model.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model verification failed. Please retry the download.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, ModelPath(id), true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task InstallBundledAsync(string bundle, CancellationToken token)
    {
        if (!IsInstalled(ZipformerModel.Id)) await InstallBundleAsync(ZipformerModel.Id, bundle, new Progress<double>(), token);
    }
    private async Task InstallBundleAsync(string id, string? bundle, IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        var staging = ModelPath(id) + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(staging);
        try
        {
            long completed = 0;
            foreach (var item in Files(id))
            {
                using var response = bundle is null ? await http.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, token) : null;
                response?.EnsureSuccessStatusCode();
                await using var input = bundle is null ? await response!.Content.ReadAsStreamAsync(token) : File.OpenRead(Path.Combine(bundle, item.Name));
                await using (var output = File.Create(Path.Combine(staging, item.Name)))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[131072]; long bytes = 0; int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        bytes += read;
                        if (bytes > item.Bytes) throw new InvalidDataException("Unexpected model size.");
                        hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token);
                        progress.Report((completed + bytes) * 100d / Find(id).Bytes);
                    }
                    if (bytes != item.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(item.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Speech model verification failed.");
                }
                completed += item.Bytes;
            }
            if (Find(id).Bundle is { } metadata)
                await File.WriteAllTextAsync(Path.Combine(staging, "NOTICE.txt"), $"{Find(id).Name}\nPublisher: {metadata.Publisher}\nLicense: {metadata.License}\nOriginal: {metadata.Origin}\nConverted INT8 ONNX files: {metadata.Source}\nExport/quantization changes format and weight precision.\n", token);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(ModelPath(id))) Directory.Delete(ModelPath(id), true);
            Directory.Move(staging, ModelPath(id));
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public IReadOnlyList<LocalModelFile> Files(string id) => Find(id).Bundle?.Files ?? (id == ZipformerModel.Id
        ? ZipformerModel.Files.Select(f => new LocalModelFile(f.Name, f.Bytes, f.Hash, ZipformerModel.Url(f.Name))).ToArray()
        : []);
    public void Remove(string id)
    {
        if (Find(id).IsFolder) { if (Directory.Exists(ModelPath(id))) Directory.Delete(ModelPath(id), true); }
        else File.Delete(ModelPath(id));
    }
}
