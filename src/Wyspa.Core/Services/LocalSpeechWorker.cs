using System.Collections.Concurrent;

namespace Wyspa.Core.Services;

// One sleeping, below-normal worker per detector instead of a new thread for every audio frame.
internal sealed class LocalSpeechWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    public LocalSpeechWorker(string name)
    {
        new Thread(() => { foreach (var action in _work.GetConsumingEnumerable()) action(); })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = name }.Start();
    }
    public Task<T> Run<T>(Func<T> action, CancellationToken token)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _work.Add(() =>
            {
                try { token.ThrowIfCancellationRequested(); result.TrySetResult(action()); }
                catch (OperationCanceledException) { result.TrySetCanceled(token); }
                catch (Exception ex) { result.TrySetException(ex); }
            }, token);
        }
        catch (OperationCanceledException) { result.TrySetCanceled(token); }
        catch (InvalidOperationException) { result.TrySetException(new ObjectDisposedException(nameof(LocalSpeechWorker))); }
        return result.Task;
    }
    public void Dispose() => _work.CompleteAdding();
}
