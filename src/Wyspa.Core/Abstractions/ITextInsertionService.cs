using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface ITextInsertionService
{
    Task<bool> InsertAsync(string text, InsertionMode mode, bool copyToClipboardOnFailure, CancellationToken cancellationToken);
}
