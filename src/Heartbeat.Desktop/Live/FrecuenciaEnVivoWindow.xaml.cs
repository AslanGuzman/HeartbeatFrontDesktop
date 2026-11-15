using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.AspNetCore.SignalR.Client;

namespace Heartbeat.Desktop.Live;

/// <summary>
/// Frecuencia cardíaca del Galaxy Watch 5 en tiempo real: historial de los últimos 5 minutos por HTTP y después
/// cada lectura por SignalR (<c>/hubs/heartrate</c>, evento <c>HeartRate</c>).
/// </summary>
public partial class FrecuenciaEnVivoWindow : Window
{
    private const int MaxReadings = 300;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _baseUri;
    private readonly List<HeartRateReading> _readings = new();
    private readonly DispatcherTimer _staleTimer;
    private readonly CancellationTokenSource _cts = new();
    private HubConnection? _hub;
    private DateTimeOffset? _lastDataAt;
    private string _connection = "conectando…";

    public FrecuenciaEnVivoWindow()
        : this(LiveBackend.ResolveBaseUrl())
    {
    }

    public FrecuenciaEnVivoWindow(string baseUrl)
    {
        InitializeComponent();
        _baseUri = LiveBackend.ToBaseUri(baseUrl);
        _staleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _staleTimer.Tick += (_, _) => RefreshStatus();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _staleTimer.Start();
            await LoadHistoryAsync();
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            SetConnection($"error: {ex.Message}");
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            using var http = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(8) };
            List<HeartRateReading>? history =
                await http.GetFromJsonAsync<List<HeartRateReading>>("api/heartrate/recent?seconds=300", Json, _cts.Token);
            if (history is { Count: > 0 })
            {
                _readings.AddRange(history.TakeLast(MaxReadings));
                HeartRateReading last = _readings[^1];
                _lastDataAt = last.MeasuredAt;
                ShowReading(last);
                Redraw();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            SetConnection($"no se pudo cargar el historial ({ex.Message})");
        }
    }

    private async Task ConnectAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseUri, "hubs/heartrate"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<HeartRateReading>("HeartRate", reading => Dispatcher.InvokeAsync(() => OnReading(reading)));
        _hub.Reconnecting += _ => { SetConnectionFromAnyThread("reconectando…"); return Task.CompletedTask; };
        _hub.Reconnected += _ => { SetConnectionFromAnyThread("en vivo"); return Task.CompletedTask; };
        _hub.Closed += ex => { SetConnectionFromAnyThread(ex is null ? "desconectado" : $"desconectado: {ex.Message}"); return Task.CompletedTask; };

        // WithAutomaticReconnect solo reintenta conexiones ya establecidas: el primer arranque se reintenta aquí.
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                SetConnection("conectando…");
                await _hub.StartAsync(_cts.Token);
                SetConnection("en vivo");
                return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                SetConnection($"sin conexión con el backend ({ex.Message}); reintentando…");
            }

            try
            {
                await Task.Delay(RetryDelay, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnReading(HeartRateReading reading)
    {
        _readings.Add(reading);
        if (_readings.Count > MaxReadings)
        {
            _readings.RemoveRange(0, _readings.Count - MaxReadings);
        }

        _lastDataAt = DateTimeOffset.Now;
        ShowReading(reading);
        Redraw();
        RefreshStatus();
    }

    private void ShowReading(HeartRateReading reading)
    {
        BpmText.Text = reading.Bpm.ToString(System.Globalization.CultureInfo.CurrentCulture);
        MeasuredText.Text = $"medida a las {reading.MeasuredAt.ToLocalTime():HH:mm:ss}";
    }

    private void SetConnectionFromAnyThread(string state) => Dispatcher.InvokeAsync(() => SetConnection(state));

    private void SetConnection(string state)
    {
        _connection = state;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool stale = _lastDataAt is null || DateTimeOffset.Now - _lastDataAt.Value > StaleAfter;
        bool live = _hub?.State == HubConnectionState.Connected;
        StatusText.Text = live && stale ? "sin datos hace más de 10 s" : _connection;
        StaleWarning.Visibility = live && stale ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        double width = ChartCanvas.ActualWidth;
        double height = ChartCanvas.ActualHeight;
        var points = new PointCollection();
        if (_readings.Count > 0 && width > 0 && height > 0)
        {
            int min = _readings.Min(r => r.Bpm) - 5;
            int max = _readings.Max(r => r.Bpm) + 5;
            double range = Math.Max(1, max - min);
            double step = width / Math.Max(1, MaxReadings - 1);
            double offset = width - ((_readings.Count - 1) * step); // las lecturas más recientes quedan a la derecha
            for (int i = 0; i < _readings.Count; i++)
            {
                double y = height - ((_readings[i].Bpm - min) / range * height);
                points.Add(new Point(offset + (i * step), y));
            }
        }

        ChartLine.Points = points;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _staleTimer.Stop();
        await _cts.CancelAsync();
        try
        {
            if (_hub is not null)
            {
                await _hub.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // La ventana ya está cerrada: un fallo al cerrar la conexión no debe tumbar la app.
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
