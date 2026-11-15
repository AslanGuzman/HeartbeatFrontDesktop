using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Sincronización simulada del modo demostración: lo pendiente (check-ups, mensajes y ediciones de perfil) "sale" en
/// cuanto hay enlace y se acumula mientras no lo hay (MR-10, MR-11). Alimenta la barra de estado. No envía nada a
/// ningún servidor: con la Crew API real se usa <see cref="PendingOnlySyncStatusProvider"/> hasta que el motor de
/// sincronización (#32) lo sustituya.
/// </summary>
public sealed class DemoSyncStatusProvider : ISyncStatusProvider
{
    private readonly IHealthRecordStore _store;
    private readonly IProfileStore _profiles;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastProfileSync;

    public DemoSyncStatusProvider(IHealthRecordStore store, IProfileStore profiles, IConnectivityService connectivity, TimeProvider clock)
    {
        _store = store;
        _profiles = profiles;
        _connectivity = connectivity;
        _clock = clock;

        _store.Changed += OnStoreChanged;
        _profiles.Changed += OnStoreChanged;
        _connectivity.IsOnlineChanged += OnConnectivityChanged;
    }

    public DateTimeOffset? LastSyncAt
    {
        get
        {
            DateTimeOffset? stored = _store.LastSyncedAt;
            return stored is null ? _lastProfileSync : _lastProfileSync > stored ? _lastProfileSync : stored;
        }
    }

    public int PendingCount => _store.PendingCount + _profiles.PendingCount;

    public event EventHandler? Changed;

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        FlushIfOnline();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectivityChanged(object? sender, EventArgs e) => FlushIfOnline();

    private void FlushIfOnline()
    {
        if (!_connectivity.IsOnline)
        {
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        if (_store.PendingCount > 0)
        {
            _store.MarkAllSynced(now);
        }

        if (_profiles.PendingCount > 0 && _profiles.MarkAllSynced() > 0)
        {
            _lastProfileSync = now;
        }
    }
}

/// <summary>
/// Estado de sincronización con la Crew API real mientras no exista el motor de sync (#32): informa de lo pendiente,
/// sin marcar nada como enviado, porque todavía nada lo envía.
/// </summary>
public sealed class PendingOnlySyncStatusProvider : ISyncStatusProvider
{
    private readonly IHealthRecordStore _store;
    private readonly IProfileStore _profiles;

    public PendingOnlySyncStatusProvider(IHealthRecordStore store, IProfileStore profiles)
    {
        _store = store;
        _profiles = profiles;
        _store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _profiles.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public DateTimeOffset? LastSyncAt => _store.LastSyncedAt;

    public int PendingCount => _store.PendingCount + _profiles.PendingCount;

    public event EventHandler? Changed;
}
