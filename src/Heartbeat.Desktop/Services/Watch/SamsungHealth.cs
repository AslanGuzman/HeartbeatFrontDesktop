using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Heartbeat.Client;
using Heartbeat.Desktop.Live;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Salud;
using Heartbeat.Desktop.Services.Auth;

namespace Heartbeat.Desktop.Services.Watch;

/// <summary>Lo último que devolvió <c>/api/salud</c>; cada parte es <c>null</c> si esa llamada falló.</summary>
public sealed record SamsungHealthData(
    DailySummaryDto? Summary,
    List<DailyActivityDto>? Activity,
    List<SleepRecordDto>? Sleep,
    List<SyncStatusDto>? SyncStatus,
    IReadOnlyList<string> Errors);

/// <summary>Datos de Samsung Health (no en tiempo real) que el backend lee de WatchSync y expone en <c>/api/salud</c>.</summary>
public interface ISamsungHealthClient
{
    Task<SamsungHealthData> LoadAsync(CancellationToken ct);
}

/// <summary>Las rutas exigen sesión: se envía el token del tripulante, que además limita los datos a su reloj.</summary>
public sealed class HttpSamsungHealthClient(CrewApiOptions options, ICrewApiClient client) : ISamsungHealthClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ApiResult<T>(T? Data, string? Error);

    public async Task<SamsungHealthData> LoadAsync(CancellationToken ct)
    {
        using HttpClient http = new() { BaseAddress = LiveBackend.ToBaseUri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (client.AccessToken is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        Task<ApiResult<DailySummaryDto>> summary = GetAsync<DailySummaryDto>(http, "api/salud/resumen", ct);
        Task<ApiResult<List<DailyActivityDto>>> activity = GetAsync<List<DailyActivityDto>>(http, "api/salud/actividad", ct);
        Task<ApiResult<List<SleepRecordDto>>> sleep = GetAsync<List<SleepRecordDto>>(http, "api/salud/sueno", ct);
        Task<ApiResult<List<SyncStatusDto>>> status = GetAsync<List<SyncStatusDto>>(http, "api/salud/estado", ct);
        await Task.WhenAll(summary, activity, sleep, status);

        string[] errors = new[] { summary.Result.Error, activity.Result.Error, sleep.Result.Error, status.Result.Error }
            .OfType<string>().Distinct().ToArray();
        return new SamsungHealthData(summary.Result.Data, activity.Result.Data, sleep.Result.Data, status.Result.Data, errors);
    }

    private static async Task<ApiResult<T>> GetAsync<T>(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                return new ApiResult<T>(await response.Content.ReadFromJsonAsync<T>(Json, ct), null);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.ServiceUnavailable => new ApiResult<T>(default, Loc.T("watch.health.unavailable")),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ApiResult<T>(default, Loc.T("watch.health.signIn")),
                _ => new ApiResult<T>(default, Loc.T("watch.health.error", (int)response.StatusCode, await ReadErrorAsync(response, ct))),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ApiResult<T>(default, Loc.T("watch.health.connect", ex.Message));
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() ?? "" : response.ReasonPhrase ?? "";
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "";
        }
    }
}
