using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

internal sealed class FakeConnectivityService(bool isOnline = true) : IConnectivityService
{
    public bool IsOnline { get; private set; } = isOnline;

    public event EventHandler? IsOnlineChanged;

    public void Set(bool isOnline)
    {
        IsOnline = isOnline;
        IsOnlineChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeSyncStatusProvider : ISyncStatusProvider
{
    public DateTimeOffset? LastSyncAt { get; private set; }

    public int PendingCount { get; private set; }

    public event EventHandler? Changed;

    public void Set(DateTimeOffset? lastSyncAt, int pendingCount)
    {
        LastSyncAt = lastSyncAt;
        PendingCount = pendingCount;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
}

internal sealed class QueueingSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public void RunPending()
    {
        while (_queue.TryDequeue(out (SendOrPostCallback Callback, object? State) item))
        {
            item.Callback(item.State);
        }
    }
}

internal sealed class DisposablePageViewModel : PageViewModel, IDisposable
{
    public override string Title => "Disposable";

    public bool IsDisposed { get; private set; }

    public string? Parameter { get; set; }

    public void Dispose() => IsDisposed = true;
}

internal sealed class FailingPageViewModel : PageViewModel
{
    public FailingPageViewModel()
    {
        throw new InvalidOperationException("No se puede crear.");
    }

    public override string Title => "Failing";
}
