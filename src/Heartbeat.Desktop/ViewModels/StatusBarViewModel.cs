using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Barra de estado: en línea / sin conexión, última sincronización y elementos pendientes.</summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    private readonly SynchronizationContext? _uiContext;
    private readonly IConnectivityService _connectivity;
    private readonly ISyncStatusProvider _syncStatus;

    /// <param name="uiContext">
    /// Contexto al que se reenvían los eventos de red y de sync, que llegan en hilos del pool. Si no se indica
    /// (lo normal con DI) se usa el del hilo que crea el ViewModel, que es el de la interfaz. Sin contexto, se
    /// actualiza directamente.
    /// </param>
    public StatusBarViewModel(
        IConnectivityService connectivity,
        ISyncStatusProvider syncStatus,
        SynchronizationContext? uiContext = null)
    {
        _connectivity = connectivity;
        _syncStatus = syncStatus;
        _uiContext = uiContext ?? SynchronizationContext.Current;

        Refresh();
        _connectivity.IsOnlineChanged += OnSourceChanged;
        _syncStatus.Changed += OnSourceChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial bool IsOnline { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSyncText))]
    public partial DateTimeOffset? LastSyncAt { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial int PendingCount { get; private set; }

    public string ConnectionText => IsOnline ? "En línea" : "Sin conexión";

    public string LastSyncText => LastSyncAt is { } at
        ? $"Último sync: {at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}"
        : "Último sync: nunca";

    public string PendingText => $"Pendientes: {PendingCount}";

    public bool HasPending => PendingCount > 0;

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null)
        {
            Refresh();
        }
        else
        {
            _uiContext.Post(static state => ((StatusBarViewModel)state!).Refresh(), this);
        }
    }

    private void Refresh()
    {
        IsOnline = _connectivity.IsOnline;
        LastSyncAt = _syncStatus.LastSyncAt;
        PendingCount = _syncStatus.PendingCount;
    }
}
