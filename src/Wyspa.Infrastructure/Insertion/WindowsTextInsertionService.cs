using System.Windows.Forms;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Infrastructure.Insertion;

public sealed class WindowsTextInsertionService : ITextInsertionService
{
    public async Task<bool> InsertAsync(string text, InsertionMode mode, bool copyToClipboardOnFailure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        try
        {
            if (mode is InsertionMode.Type)
            {
                SendKeys.SendWait(EscapeForSendKeys(text));
                return true;
            }

            await PasteWithClipboardAsync(text, cancellationToken);
            return true;
        }
        catch
        {
            if (copyToClipboardOnFailure)
            {
                CopyToClipboard(text);
            }

            return false;
        }
    }

    private static void CopyToClipboard(string text)
    {
        Clipboard.SetText(text);
    }

    private static async Task PasteWithClipboardAsync(string text, CancellationToken cancellationToken)
    {
        IDataObject? previous = null;
        var pasteSent = false;

        try
        {
            if (Clipboard.ContainsData(DataFormats.Text) ||
                Clipboard.ContainsData(DataFormats.Bitmap) ||
                Clipboard.ContainsData(DataFormats.FileDrop))
            {
                previous = Clipboard.GetDataObject();
            }

            Clipboard.SetText(text);
            SendKeys.SendWait("^v");
            pasteSent = true;

            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException) when (pasteSent)
            {
            }
        }
        catch (Exception) when (pasteSent)
        {
        }
        finally
        {
            if (previous is not null)
            {
                TryRestoreClipboard(previous);
            }
        }
    }

    private static void TryRestoreClipboard(IDataObject previous)
    {
        try
        {
            Clipboard.SetDataObject(previous, copy: true);
        }
        catch
        {
            // A clipboard restore failure can happen after Ctrl+V already succeeded.
        }
    }

    private static string EscapeForSendKeys(string text)
    {
        return text
            .Replace("{", "{{}", StringComparison.Ordinal)
            .Replace("}", "{}}", StringComparison.Ordinal)
            .Replace("+", "{+}", StringComparison.Ordinal)
            .Replace("^", "{^}", StringComparison.Ordinal)
            .Replace("%", "{%}", StringComparison.Ordinal)
            .Replace("~", "{~}", StringComparison.Ordinal)
            .Replace("(", "{(}", StringComparison.Ordinal)
            .Replace(")", "{)}", StringComparison.Ordinal);
    }
}
