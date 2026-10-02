using System.Collections.Concurrent;
using System.Windows.Automation;

namespace Wyspa.Infrastructure.Insertion;

// UIA event registration/removal must be serialized on a non-UI MTA thread.
// Removing a handler on the dispatcher can stall both the overlay and input.
internal static class AutomationEventThread
{
    private static readonly BlockingCollection<Action> Work = new();

    static AutomationEventThread()
    {
        var thread = new Thread(() =>
        {
            foreach (var action in Work.GetConsumingEnumerable()) action();
        }) { IsBackground = true, Name = "Wyspa UI Automation events" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public static Task AddAsync(AutomationFocusChangedEventHandler handler) =>
        Enqueue(() => Automation.AddAutomationFocusChangedEventHandler(handler));

    public static void Remove(AutomationFocusChangedEventHandler handler)
    {
        _ = Enqueue(() =>
        {
            try { Automation.RemoveAutomationFocusChangedEventHandler(handler); }
            catch { /* A vanished provider must not interrupt the next session. */ }
        });
    }

    private static Task Enqueue(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }
}
