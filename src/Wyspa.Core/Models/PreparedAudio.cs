namespace Wyspa.Core.Models;

/// <summary>Owns only generated audio. The user's source file is never deleted.</summary>
public sealed class PreparedAudio(
    IReadOnlyList<string> paths, long originalBytes, long uploadBytes, string description,
    string? temporaryDirectory = null, IReadOnlyList<double>? partStartSeconds = null) : IDisposable
{
    public IReadOnlyList<string> Paths { get; } = paths;
    /// <summary>Exact offsets in the source audio, including silence, for each upload part.</summary>
    public IReadOnlyList<double> PartStartSeconds { get; } = ValidateOffsets(paths, partStartSeconds);
    public long OriginalBytes { get; } = originalBytes;
    public long UploadBytes { get; } = uploadBytes;
    public string Description { get; } = description;

    private static IReadOnlyList<double> ValidateOffsets(IReadOnlyList<string> paths, IReadOnlyList<double>? offsets)
    {
        offsets ??= paths.Count == 1 ? [0d] : throw new ArgumentException("Split audio requires source offsets.", nameof(offsets));
        if (offsets.Count != paths.Count || offsets.Any(v => !double.IsFinite(v) || v < 0) ||
            offsets.Where((v, i) => i > 0 && v < offsets[i - 1]).Any())
            throw new ArgumentException("Each audio part requires an ordered, nonnegative source offset.", nameof(offsets));
        return offsets.ToArray();
    }

    public void Dispose()
    {
        if (temporaryDirectory is not null && Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
