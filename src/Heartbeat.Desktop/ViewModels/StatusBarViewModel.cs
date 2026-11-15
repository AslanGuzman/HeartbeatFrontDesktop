using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Localization;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RejectedText), nameof(HasRejected))]
    public partial int RejectedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStateText), nameof(HasSyncState))]
    public partial bool IsSyncing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStateText), nameof(HasSyncState))]
    public partial string? SyncStatusText { get; private set; }

    /// <summary>«Sincronizando…» mientras corre, o el aviso del último intento («Sin respuesta del servidor…»); vacío si todo va bien.</summary>
    public string SyncStateText => IsSyncing ? Loc.T("bar.syncing") : SyncStatusText ?? string.Empty;

    public bool HasSyncState => SyncStateText.Length > 0;

    public string RejectedText => RejectedCount == 1 ? Loc.T("bar.rejected.one") : Loc.T("bar.rejected.many", RejectedCount);

    public bool HasRejected => RejectedCount > 0;

    /// <summary>Pide una sincronización ahora sin esperar a los 60 s.</summary>
    [RelayCommand]
    private void SyncNow() => _syncStatus.SyncNow();

    public string ConnectionText => IsOnline ? Loc.T("bar.online") : Loc.T("bar.offline");

    public string LastSyncText => LastSyncAt is { } at
        ? Loc.T("bar.lastSync", at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : Loc.T("bar.neverSynced");

    public string PendingText => Loc.T("bar.pending", PendingCount);

    public bool HasPending => PendingCount > 0;

    /// <summary>Vuelve a leer todos los textos tras cambiar el idioma.</summary>
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

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
        RejectedCount = _syncStatus.RejectedCount;
        IsSyncing = _syncStatus.IsSyncing;
        SyncStatusText = _syncStatus.StatusText;
    }
}
