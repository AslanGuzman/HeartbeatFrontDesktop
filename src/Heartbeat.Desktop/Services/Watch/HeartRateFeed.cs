using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Heartbeat.Client;
using Heartbeat.Desktop.Live;
using Heartbeat.Desktop.Services.Auth;

using Microsoft.AspNetCore.SignalR.Client;

namespace Heartbeat.Desktop.Services.Watch;

public enum WatchFeedState
{
    Stopped,
    Connecting,
    Live,
    Reconnecting,
    Failed,
}

/// <summary>
/// La frecuencia cardíaca del reloj en vivo: el historial reciente por HTTP y cada lectura por SignalR. Se conecta con el
/// token del tripulante, así que el backend solo entrega la FC de SU reloj.
/// </summary>
public interface IHeartRateFeed : IDisposable
{
    WatchFeedState State { get; }

    /// <summary>Llega en un hilo del pool.</summary>
    event EventHandler<HeartRateReading>? ReadingReceived;

    /// <summary>Llega en un hilo del pool.</summary>
    event EventHandler? StateChanged;

    /// <summary>Últimos 5 minutos; vacío si no se pudo leer.</summary>
    Task<IReadOnlyList<HeartRateReading>> LoadRecentAsync(CancellationToken ct);

    /// <summary>Empieza a conectarse (y a reintentar). No bloquea.</summary>
    void Start();

    void Stop();
}

public sealed class SignalRHeartRateFeed(CrewApiOptions options, ICrewApiClient client) : IHeartRateFeed
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _baseUri = LiveBackend.ToBaseUri(options.BaseUrl);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private HubConnection? _hub;
    private WatchFeedState _state;

    public WatchFeedState State => _state;

    public event EventHandler<HeartRateReading>? ReadingReceived;

    public event EventHandler? StateChanged;

    public async Task<IReadOnlyList<HeartRateReading>> LoadRecentAsync(CancellationToken ct)
    {
        try
        {
            using HttpClient http = new() { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(8) };
            if (client.AccessToken is { Length: > 0 } token)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await http.GetFromJsonAsync<List<HeartRateReading>>("api/heartrate/recent?seconds=300", Json, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            _ = Task.Run(() => RunAsync(ct), ct);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        HubConnection? hub;
        lock (_gate)
        {
            cts = _cts;
            hub = _hub;
            _cts = null;
            _hub = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
        if (hub is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await hub.DisposeAsync();
                }
                catch (Exception)
                {
                    // Cerrar la conexión nunca debe tumbar la app.
                }
            });
        }

        SetState(WatchFeedState.Stopped);
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken ct)
    {
        HubConnection hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseUri, "hubs/heartrate"), o => o.AccessTokenProvider = () => Task.FromResult(client.AccessToken))
            .WithAutomaticReconnect()
            .Build();
        lock (_gate)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _hub = hub;
        }

        hub.On<HeartRateReading>("HeartRate", reading => ReadingReceived?.Invoke(this, reading));
        hub.Reconnecting += _ => { SetState(WatchFeedState.Reconnecting); return Task.CompletedTask; };
        hub.Reconnected += _ => { SetState(WatchFeedState.Live); return Task.CompletedTask; };
        hub.Closed += _ => { SetState(WatchFeedState.Failed); return Task.CompletedTask; };

        // WithAutomaticReconnect solo reintenta conexiones ya establecidas: el primer arranque se reintenta aquí.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(WatchFeedState.Connecting);
                await hub.StartAsync(ct);
                SetState(WatchFeedState.Live);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                SetState(WatchFeedState.Failed);
            }

            try
            {
                await Task.Delay(RetryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void SetState(WatchFeedState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
