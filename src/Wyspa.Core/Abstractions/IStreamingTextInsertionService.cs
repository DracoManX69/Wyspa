using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IStreamingTextInsertionService
{
    // Called on the UI thread. Each listening session owns its cumulative clipboard.
    void BeginStream(InsertionMode mode = InsertionMode.Paste);
    Task<bool> AppendStreamAsync(string delta, string cumulativeText, CancellationToken cancellationToken);
    // Returns false when the final version is available only on the clipboard.
    Task<bool> CompleteStreamAsync(string finalText, bool allowCorrection, CancellationToken cancellationToken);
    void EndStream();
}
