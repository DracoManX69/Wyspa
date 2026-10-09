using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IConversationCapture : IAsyncDisposable
{
    event EventHandler<AudioChunk>? ChunkAvailable;
    event EventHandler<string>? Failed;
    bool SupportsApplicationCapture { get; }
    IReadOnlyList<CaptureTarget> GetOutputs();
    IReadOnlyList<CaptureTarget> GetApplications();
    Task StartAsync(ConversationCaptureOptions options, double offset, CancellationToken token);
    Task StopAsync();
}

public interface ISpeakerIdentifier : IDisposable
{
    Task InitializeAsync(CancellationToken token, IProgress<string>? progress = null);
    Task<IReadOnlyList<SpeakerTurn>> IdentifyAsync(float[] samples, CancellationToken token);
    void Reset();
}

public interface INoteIntelligence
{
    Task<SpeechResult> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token);
    Task<string> SummariseAsync(string key, string transcript, string model, CancellationToken token);
}

public interface INoteStore
{
    string DirectoryPath { get; }
    Task SaveAsync(NoteSession note, CancellationToken token = default);
    Task<IReadOnlyList<NoteSession>> LoadAsync(CancellationToken token = default);
    Task DeleteAsync(Guid id, CancellationToken token = default);
}

public interface IVideoImporter
{
    Task<PreparedVideo> PrepareAsync(string url, IProgress<string> progress, CancellationToken token);
}

public sealed class PreparedVideo(string title, string directory, IReadOnlyList<string> parts) : IDisposable
{
    public string Title { get; } = title;
    public IReadOnlyList<string> Parts { get; } = parts;
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
