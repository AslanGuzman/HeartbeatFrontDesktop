using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;
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

    public int RejectedCount { get; private set; }

    public bool IsSyncing { get; private set; }

    public string? StatusText { get; private set; }

    public int SyncRequests { get; private set; }

    public event EventHandler? Changed;

    public void SyncNow() => SyncRequests++;

    public void Set(DateTimeOffset? lastSyncAt, int pendingCount, int rejectedCount = 0, bool isSyncing = false, string? statusText = null)
    {
        LastSyncAt = lastSyncAt;
        PendingCount = pendingCount;
        RejectedCount = rejectedCount;
        IsSyncing = isSyncing;
        StatusText = statusText;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Sincronización instantánea de pruebas: en cuanto hay enlace, todo lo pendiente "sale" y se marca sincronizado, como si
/// un servidor lo aceptara al momento. Sirve para las pruebas de pantallas; el motor real se prueba con <see cref="SyncTestWorld"/>.
/// </summary>
internal sealed class InstantSyncStatusProvider : ISyncStatusProvider
{
    private readonly IHealthRecordStore _store;
    private readonly IProfileStore _profiles;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastProfileSync;

    public InstantSyncStatusProvider(IHealthRecordStore store, IProfileStore profiles, IConnectivityService connectivity, TimeProvider clock)
    {
        _store = store;
        _profiles = profiles;
        _connectivity = connectivity;
        _clock = clock;
        _store.Changed += OnChanged;
        _profiles.Changed += OnChanged;
        _connectivity.IsOnlineChanged += (_, _) => Flush();
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

    public int RejectedCount => 0;

    public bool IsSyncing => false;

    public string? StatusText => null;

    public event EventHandler? Changed;

    public void SyncNow() => Flush();

    private void OnChanged(object? sender, EventArgs e)
    {
        Flush();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Flush()
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

internal sealed class FakeUrlLauncher(bool succeeds = true) : IUrlLauncher
{
    public List<Uri> Opened { get; } = [];

    public bool Succeeds { get; set; } = succeeds;

    public bool TryOpen(Uri url)
    {
        Opened.Add(url);
        return Succeeds;
    }
}

internal sealed class FakeIndicatorInfoPresenter : IIndicatorInfoPresenter
{
    public List<string?> Shown { get; } = [];

    public void Show(string? indicatorId = null) => Shown.Add(indicatorId);
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

internal sealed class PlainPageViewModel : PageViewModel
{
    public override string Title => "Plain";
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Navegación de pruebas: crea la página con una fábrica y registra a dónde se navegó.</summary>
internal sealed class RecordingNavigation(Func<Type, PageViewModel> factory) : INavigationService
{
    public PageViewModel? CurrentPage { get; private set; }

    public List<Type> Visited { get; } = [];

    public event EventHandler? CurrentPageChanged;

    public PageViewModel NavigateTo(Type pageType, Action<PageViewModel>? initialize = null)
    {
        PageViewModel page = factory(pageType);
        initialize?.Invoke(page);
        CurrentPage = page;
        Visited.Add(pageType);
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        return page;
    }
}
