using System.Threading;

namespace Wyspa.App.Services;

public sealed class AppLifecycleService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _showSignalName;
    private readonly string _quitSignalName;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _quitSignal;
    private RegisteredWaitHandle? _showRegistration;
    private RegisteredWaitHandle? _quitRegistration;

    public AppLifecycleService(string mutexName, string showSignalName, string quitSignalName)
    {
        _mutexName = mutexName;
        _showSignalName = showSignalName;
        _quitSignalName = quitSignalName;
    }

    public bool TryStart(Action showSettings, Action quit)
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (!createdNew)
        {
            return false;
        }

        _ownsMutex = true;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, _showSignalName);
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, _quitSignalName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => showSettings(),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);
        _quitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _quitSignal,
            (_, _) => quit(),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);
        return true;
    }

    public void SignalExistingShow()
    {
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, _showSignalName);
        signal.Set();
    }

    public void SignalExistingQuit()
    {
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, _quitSignalName);
        signal.Set();
    }

    public void Dispose()
    {
        _showRegistration?.Unregister(null);
        _quitRegistration?.Unregister(null);
        _showSignal?.Dispose();
        _quitSignal?.Dispose();
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
    }
}
