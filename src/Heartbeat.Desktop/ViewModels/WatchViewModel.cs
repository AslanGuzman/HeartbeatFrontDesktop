using System.Globalization;
using System.Net.Http;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Live;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Salud;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Watch;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una tarjeta del resumen de Samsung Health (por ejemplo «Frecuencia cardíaca · 70 lpm · Medido: …»).</summary>
public sealed record HealthCardViewModel(string Title, string Value, string Measured);

/// <summary>Una fila de la tabla de actividad de los últimos días.</summary>
public sealed record ActivityRowViewModel(string Date, string Steps, string Distance, string ActiveCalories, string ActiveMinutes);

/// <summary>
/// Reloj (Galaxy Watch 5): la FC en vivo y los datos de Samsung Health en una sola pantalla del menú (WR-DESKTOP).
/// La FC llega por el hub con el token del tripulante, así que solo se ve la de su reloj. Sin enlace, o mientras el reloj
/// no envía, se muestra la última lectura guardada con su hora y el aviso de que no hay datos del reloj. Todos los
/// textos se calculan al leerlos, de modo que cambiar el idioma no corta la conexión.
/// </summary>
public sealed partial class WatchViewModel : PageViewModel, IDisposable
{
    public const int MaxSamples = 300;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(5);

    private readonly IHeartRateFeed _feed;
    private readonly ISamsungHealthClient _health;
    private readonly ILastWatchReadingStore _lastStore;
    private readonly ISessionContext _session;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _uiContext;
    private readonly ITimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<int> _samples = [];

    private HeartRateReading? _latest;
    private bool _latestIsSaved;
    private DateTimeOffset? _lastDataAt;
    private DateTimeOffset _lastSavedAt = DateTimeOffset.MinValue;
    private SamsungHealthData? _healthData;
    private bool _healthLoaded;
    private bool _disposed;

    /// <param name="uiContext">Contexto al que se reenvían los eventos del reloj y de la red (por defecto, el del hilo que crea la pantalla).</param>
    public WatchViewModel(
        IHeartRateFeed feed,
        ISamsungHealthClient health,
        ILastWatchReadingStore lastStore,
        ISessionContext session,
        IConnectivityService connectivity,
        TimeProvider clock,
        SynchronizationContext? uiContext = null)
    {
        _feed = feed;
        _health = health;
        _lastStore = lastStore;
        _session = session;
        _connectivity = connectivity;
        _clock = clock;
        _uiContext = uiContext ?? SynchronizationContext.Current;
        IsLiveTab = true;

        // Lo último que se supo del reloj, para tener algo que enseñar sin enlace.
        if (session.CurrentUser is { } user && lastStore.Get(user.Id) is { } saved)
        {
            _latest = saved;
            _latestIsSaved = true;
        }

        _feed.ReadingReceived += OnReadingReceived;
        _feed.StateChanged += OnFeedStateChanged;
        _connectivity.IsOnlineChanged += OnConnectivityChanged;
        _timer = _clock.CreateTimer(_ => Post(RefreshStatus), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        if (_connectivity.IsOnline)
        {
            _ = StartFeedAsync();
        }
    }

    public override string Title => Loc.T("nav.watch");

    /// <summary>Los textos son propiedades calculadas: al cambiar el idioma basta con releerlos.</summary>
    public override bool RebuildsOnLanguageChange => false;

    /// <summary>Aviso fijo y siempre visible en esta pantalla: lo que mide el reloj no es una medición clínica.</summary>
    public string DisclaimerText => Loc.T("watch.disclaimer");

    // ---- Pestañas ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthTab))]
    public partial bool IsLiveTab { get; private set; }

    public bool IsHealthTab => !IsLiveTab;

    [RelayCommand]
    private void ShowLive() => IsLiveTab = true;

    [RelayCommand]
    private async Task ShowHealthAsync()
    {
        IsLiveTab = false;
        if (!_healthLoaded)
        {
            await LoadHealthAsync();
        }
    }

    // ---- FC en vivo ----

    public string BpmText => _latest is { } r ? r.Bpm.ToString(CultureInfo.CurrentCulture) : "--";

    public string BpmUnit => Loc.T("unit.heartRate");

    /// <summary>La FC (en lpm) de las últimas lecturas, para la línea de la gráfica.</summary>
    public IReadOnlyList<int> Samples => _samples.ToArray();

    /// <summary>Cuándo se midió la lectura que se ve; si es la guardada, lo dice.</summary>
    public string MeasuredText
    {
        get
        {
            if (_latest is not { } r)
            {
                return Loc.T("watch.noReadings");
            }

            return _latestIsSaved
                ? Loc.T("watch.lastSaved", r.MeasuredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : Loc.T("watch.measuredAt", r.MeasuredAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        }
    }

    /// <summary>El reloj está enviando ahora mismo: conectado y con una lectura de hace menos de 10 s.</summary>
    public bool IsFresh => _connectivity.IsOnline
        && _feed.State == WatchFeedState.Live
        && _lastDataAt is { } at
        && _clock.GetUtcNow() - at <= StaleAfter;

    public string StatusText
    {
        get
        {
            if (!_connectivity.IsOnline)
            {
                return Loc.T("watch.status.offline");
            }

            return _feed.State switch
            {
                WatchFeedState.Live when !IsFresh => Loc.T("watch.status.stale"),
                WatchFeedState.Live => Loc.T("watch.status.live"),
                WatchFeedState.Reconnecting => Loc.T("watch.status.reconnecting"),
                WatchFeedState.Failed => Loc.T("watch.status.failed"),
                WatchFeedState.Stopped => Loc.T("watch.status.stopped"),
                _ => Loc.T("watch.status.connecting"),
            };
        }
    }

    public bool ShowNoDataNotice => !IsFresh;

    /// <summary>«Sin datos del reloj…»: sin enlace explica que se muestra la última lectura guardada.</summary>
    public string NoDataNoticeText => _connectivity.IsOnline ? Loc.T("watch.noData.stale") : Loc.T("watch.noData.offline");

    // ---- Samsung Health ----

    [ObservableProperty]
    public partial bool IsHealthLoading { get; private set; }

    public IReadOnlyList<HealthCardViewModel> HealthCards
    {
        get
        {
            LatestValuesDto latest = _healthData?.Summary?.Latest ?? new LatestValuesDto();
            return
            [
                Card(Loc.T("ind.heartRate"), latest.HeartRate, v => $"{v.Value:0} {v.Unit ?? Loc.T("unit.heartRate")}"),
                Card("SpO₂", latest.Spo2, v => $"{v.Value:0} {v.Unit ?? "%"}"),
                Card(Loc.T("ind.bloodPressure"), latest.BloodPressure, v => $"{v.Value:0}/{v.Value2?.ToString("0", CultureInfo.CurrentCulture) ?? "?"} {v.Unit ?? "mmHg"}"),
                Card(Loc.T("watch.skinTemp"), latest.SkinTemperature, v => $"{v.Value:0.0} {v.Unit ?? "°C"}"),
                Card(Loc.T("watch.weight"), latest.WeightKg, v => $"{v.Value:0.0} {v.Unit ?? "kg"}"),
                Card("Energy Score", latest.EnergyScore, v => $"{v.Value:0}"),
            ];
        }
    }

    public string StepsTodayText
    {
        get
        {
            DateOnly today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            int? steps = _healthData?.Summary?.Activity?.Steps ?? _healthData?.Activity?.FirstOrDefault(d => d.LocalDate == today)?.Steps;
            return Loc.T("watch.stepsToday", steps is { } s ? s.ToString("N0", CultureInfo.CurrentCulture) : Loc.T("status.noData"));
        }
    }

    public IReadOnlyList<ActivityRowViewModel> ActivityRows =>
        (_healthData?.Activity ?? [])
            .OrderByDescending(d => d.LocalDate)
            .Take(7)
            .Select(d => new ActivityRowViewModel(
                d.LocalDate.ToString("ddd dd/MM", Loc.Culture),
                d.Steps?.ToString("N0", CultureInfo.CurrentCulture) ?? "—",
                d.DistanceM is { } m ? $"{m / 1000:0.00} km" : "—",
                d.ActiveCaloriesKcal is { } kcal ? $"{kcal:0} kcal" : "—",
                d.ActiveMinutes is { } min ? $"{min} min" : "—"))
            .ToList();

    private SleepRecordDto? Night => _healthData?.Summary?.Sleep ?? _healthData?.Sleep?.OrderByDescending(n => n.EndAt).FirstOrDefault();

    public string SleepText => Night is { } n
        ? $"{n.DurationMin / 60} h {n.DurationMin % 60} min  ·  {n.StartAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} → {n.EndAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}"
        : Loc.T("status.noData");

    public string SleepStagesText => Night?.StageMinutes is { } st
        ? Loc.T("watch.sleepStages", st.Awake, st.Light, st.Deep, st.Rem)
        : string.Empty;

    public string SyncText
    {
        get
        {
            if (_healthData?.SyncStatus is not { Count: > 0 } status)
            {
                return Loc.T("watch.sync.none");
            }

            int minutes = Math.Max(0, (int)(_clock.GetUtcNow() - status.Max(s => s.LastSyncedAt)).TotalMinutes);
            return Loc.T("watch.sync.ago", minutes);
        }
    }

    public string HealthErrorText => !_connectivity.IsOnline && _healthData is null
        ? Loc.T("watch.health.offline")
        : _healthData is { Errors.Count: > 0 } d ? string.Join(Environment.NewLine, d.Errors) : string.Empty;

    public bool HasHealthError => HealthErrorText.Length > 0;

    public string HealthLoadingText => Loc.T("watch.loading");

    [RelayCommand]
    private async Task RefreshHealthAsync() => await LoadHealthAsync();

    private async Task LoadHealthAsync()
    {
        if (!_connectivity.IsOnline)
        {
            _healthLoaded = false;
            NotifyHealth();
            return;
        }

        IsHealthLoading = true;
        try
        {
            _healthData = await _health.LoadAsync(_cts.Token);
            _healthLoaded = true;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _healthData = new SamsungHealthData(null, null, null, null, [Loc.T("watch.health.connect", ex.Message)]);
        }
        finally
        {
            IsHealthLoading = false;
        }

        NotifyHealth();
    }

    private static HealthCardViewModel Card(string title, LatestValueDto? value, Func<LatestValueDto, string> format) => new(
        title,
        value is null ? Loc.T("status.noData") : format(value),
        value is null ? " " : Loc.T("watch.measuredLabel", value.MeasuredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));

    // ---- Conexión y eventos ----

    private async Task StartFeedAsync()
    {
        try
        {
            IReadOnlyList<HeartRateReading> history = await _feed.LoadRecentAsync(_cts.Token);
            if (_disposed)
            {
                return;
            }

            Post(() =>
            {
                if (history.Count > 0 && !_disposed)
                {
                    _samples.AddRange(history.TakeLast(MaxSamples).Select(r => r.Bpm));
                    HeartRateReading last = history[^1];
                    _latest = last;
                    _latestIsSaved = false;
                    _lastDataAt = last.MeasuredAt;
                    NotifyLive();
                }
            });
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (!_disposed && _connectivity.IsOnline)
        {
            _feed.Start();
        }
    }

    private void OnReadingReceived(object? sender, HeartRateReading reading) => Post(() => OnReading(reading));

    /// <summary>Una lectura nueva del reloj.</summary>
    public void OnReading(HeartRateReading reading)
    {
        if (_disposed)
        {
            return;
        }

        _samples.Add(reading.Bpm);
        if (_samples.Count > MaxSamples)
        {
            _samples.RemoveRange(0, _samples.Count - MaxSamples);
        }

        _latest = reading;
        _latestIsSaved = false;
        _lastDataAt = _clock.GetUtcNow();

        if (_lastDataAt.Value - _lastSavedAt >= SaveEvery)
        {
            Save();
            _lastSavedAt = _lastDataAt.Value;
        }

        NotifyLive();
    }

    private void OnFeedStateChanged(object? sender, EventArgs e) => Post(RefreshStatus);

    private void OnConnectivityChanged(object? sender, EventArgs e) => Post(() =>
    {
        if (_disposed)
        {
            return;
        }

        if (_connectivity.IsOnline)
        {
            _ = StartFeedAsync();
        }
        else
        {
            _feed.Stop();
        }

        RefreshStatus();
        NotifyHealth();
    });

    /// <summary>Vuelve a calcular el estado (el reloj puede dejar de enviar sin que se corte la conexión). Lo llama un temporizador cada segundo.</summary>
    public void RefreshStatus()
    {
        if (_disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsFresh));
        OnPropertyChanged(nameof(ShowNoDataNotice));
        OnPropertyChanged(nameof(NoDataNoticeText));
    }

    private void NotifyLive()
    {
        OnPropertyChanged(nameof(BpmText));
        OnPropertyChanged(nameof(MeasuredText));
        OnPropertyChanged(nameof(Samples));
        RefreshStatus();
    }

    private void NotifyHealth()
    {
        OnPropertyChanged(nameof(HealthCards));
        OnPropertyChanged(nameof(StepsTodayText));
        OnPropertyChanged(nameof(ActivityRows));
        OnPropertyChanged(nameof(SleepText));
        OnPropertyChanged(nameof(SleepStagesText));
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(HealthErrorText));
        OnPropertyChanged(nameof(HasHealthError));
    }

    private void Save()
    {
        if (_session.CurrentUser is { } user && _latest is { } reading)
        {
            _lastStore.Save(user.Id, reading);
        }
    }

    private void Post(Action action)
    {
        if (_uiContext is null)
        {
            action();
        }
        else
        {
            _uiContext.Post(static state => ((Action)state!)(), action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Lo último que se vio queda guardado para la próxima vez sin enlace.
        if (_latest is not null && !_latestIsSaved)
        {
            Save();
        }

        _disposed = true;
        _timer.Dispose();
        _feed.ReadingReceived -= OnReadingReceived;
        _feed.StateChanged -= OnFeedStateChanged;
        _connectivity.IsOnlineChanged -= OnConnectivityChanged;
        _cts.Cancel();
        _cts.Dispose();
        _feed.Dispose();
    }
}
