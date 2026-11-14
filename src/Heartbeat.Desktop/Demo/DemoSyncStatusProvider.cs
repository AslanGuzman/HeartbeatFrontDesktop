using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Sincronización simulada: lo pendiente "sale" en cuanto hay enlace, y se acumula mientras no lo hay (MR-10, MR-11).
/// Alimenta la barra de estado. El motor de sincronización real (#32) lo sustituye.
/// </summary>
public sealed class DemoSyncStatusProvider : ISyncStatusProvider
{
    private readonly IHealthRecordStore _store;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _clock;

    public DemoSyncStatusProvider(IHealthRecordStore store, IConnectivityService connectivity, TimeProvider clock)
    {
        _store = store;
        _connectivity = connectivity;
        _clock = clock;

        _store.Changed += OnStoreChanged;
        _connectivity.IsOnlineChanged += OnConnectivityChanged;
    }

    public DateTimeOffset? LastSyncAt => _store.LastSyncedAt;

    public int PendingCount => _store.PendingCount;

    public event EventHandler? Changed;

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        FlushIfOnline();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectivityChanged(object? sender, EventArgs e) => FlushIfOnline();

    private void FlushIfOnline()
    {
        if (_connectivity.IsOnline && _store.PendingCount > 0)
        {
            _store.MarkAllSynced(_clock.GetUtcNow());
        }
    }
}
