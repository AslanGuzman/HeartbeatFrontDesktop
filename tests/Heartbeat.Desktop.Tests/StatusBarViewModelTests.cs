using System.Globalization;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

public sealed class StatusBarViewModelTests
{
    private readonly FakeConnectivityService _connectivity = new();
    private readonly FakeSyncStatusProvider _sync = new();

    // xUnit instala su propio SynchronizationContext asíncrono; se inyecta uno inmediato para que las pruebas sean deterministas.
    private StatusBarViewModel Create() => new(_connectivity, _sync, new ImmediateSynchronizationContext());

    [Fact]
    public void Posts_updates_to_the_ui_context_instead_of_running_them_on_the_raising_thread()
    {
        QueueingSynchronizationContext ui = new();
        StatusBarViewModel bar = new(_connectivity, _sync, ui);

        _connectivity.Set(false);

        Assert.Equal("En línea", bar.ConnectionText);

        ui.RunPending();

        Assert.Equal("Sin conexión", bar.ConnectionText);
    }

    [Fact]
    public void Shows_online_never_synced_and_no_pending_by_default()
    {
        StatusBarViewModel bar = Create();

        Assert.Equal("En línea", bar.ConnectionText);
        Assert.Equal("Último sync: nunca", bar.LastSyncText);
        Assert.Equal("Pendientes: 0", bar.PendingText);
        Assert.False(bar.HasPending);
    }

    [Fact]
    public void Shows_offline_when_there_is_no_link()
    {
        _connectivity.Set(false);

        StatusBarViewModel bar = Create();

        Assert.False(bar.IsOnline);
        Assert.Equal("Sin conexión", bar.ConnectionText);
    }

    [Fact]
    public void Follows_connectivity_changes()
    {
        StatusBarViewModel bar = Create();
        List<string?> changed = [];
        bar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _connectivity.Set(false);

        Assert.Equal("Sin conexión", bar.ConnectionText);
        Assert.Contains(nameof(StatusBarViewModel.ConnectionText), changed);

        _connectivity.Set(true);

        Assert.Equal("En línea", bar.ConnectionText);
    }

    [Fact]
    public void Follows_sync_status_changes_and_shows_the_last_sync_in_local_time()
    {
        StatusBarViewModel bar = Create();
        DateTimeOffset lastSync = new(2026, 11, 14, 9, 35, 0, TimeSpan.Zero);

        _sync.Set(lastSync, 3);

        string expected = lastSync.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal($"Último sync: {expected}", bar.LastSyncText);
        Assert.Equal("Pendientes: 3", bar.PendingText);
        Assert.True(bar.HasPending);

        _sync.Set(lastSync, 0);

        Assert.False(bar.HasPending);
    }
}
