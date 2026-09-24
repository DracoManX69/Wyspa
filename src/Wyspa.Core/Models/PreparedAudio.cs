namespace Wyspa.Core.Models;

/// <summary>Owns only generated audio. The user's source file is never deleted.</summary>
public sealed class PreparedAudio(
    IReadOnlyList<string> paths, long originalBytes, long uploadBytes, string description,
    string? temporaryDirectory = null) : IDisposable
{
    public IReadOnlyList<string> Paths { get; } = paths;
    public long OriginalBytes { get; } = originalBytes;
    public long UploadBytes { get; } = uploadBytes;
    public string Description { get; } = description;

    public void Dispose()
    {
        if (temporaryDirectory is not null && Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
