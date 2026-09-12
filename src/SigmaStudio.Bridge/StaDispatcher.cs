using System.Collections.Concurrent;

namespace SigmaStudio.Bridge;

public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;

    public StaDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SigmaStudio-STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new WorkItem(() =>
        {
            if (cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
            else
            {
                try { completion.TrySetResult(action()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }
        }), cancellationToken);
        return completion.Task;
    }

    private void Run()
    {
        foreach (var item in _queue.GetConsumingEnumerable()) item.Action();
    }

    public void Dispose() => _queue.CompleteAdding();
    private sealed record WorkItem(Action Action);
}
