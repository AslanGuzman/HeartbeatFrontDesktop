using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using Heartbeat.Desktop.Live;

namespace Heartbeat.Desktop.Salud;

/// <summary>
/// Datos de Samsung Health (no en tiempo real) que el backend C# lee del backend Python y expone en <c>/api/salud</c>.
/// Las rutas exigen la sesión de la app: se envía el token del tripulante si lo hay.
/// </summary>
public partial class ResumenSaludWindow : Window
{
    private const string Unavailable = "El servidor de datos de Samsung Health no responde";
    private const string NoData = "Sin datos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _baseUri;
    private readonly Func<string?> _accessToken;

    public ResumenSaludWindow()
        : this(LiveBackend.ResolveBaseUrl(), () => null)
    {
    }

    public ResumenSaludWindow(string baseUrl, Func<string?> accessToken)
    {
        InitializeComponent();
        _baseUri = LiveBackend.ToBaseUri(baseUrl);
        _accessToken = accessToken ?? (() => null);
        Loaded += OnLoaded;
    }

    private sealed record ApiResult<T>(T? Data, string? Error);

    /// <summary>Fila de la tabla de actividad (solo lectura).</summary>
    public sealed record ActivityRow(string Fecha, string Pasos, string Distancia, string CaloriasActivas, string MinutosActivos);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(15) };
            if (_accessToken() is { Length: > 0 } token)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            Task<ApiResult<DailySummaryDto>> summaryTask = GetAsync<DailySummaryDto>(http, "api/salud/resumen");
            Task<ApiResult<List<DailyActivityDto>>> activityTask = GetAsync<List<DailyActivityDto>>(http, "api/salud/actividad");
            Task<ApiResult<List<SleepRecordDto>>> sleepTask = GetAsync<List<SleepRecordDto>>(http, "api/salud/sueno");
            Task<ApiResult<List<SyncStatusDto>>> statusTask = GetAsync<List<SyncStatusDto>>(http, "api/salud/estado");
            await Task.WhenAll(summaryTask, activityTask, sleepTask, statusTask);

            ShowSummary(summaryTask.Result.Data);
            ShowActivity(summaryTask.Result.Data, activityTask.Result.Data);
            ShowSleep(summaryTask.Result.Data, sleepTask.Result.Data);
            ShowSync(statusTask.Result.Data);

            string[] errors = new[] { summaryTask.Result.Error, activityTask.Result.Error, sleepTask.Result.Error, statusTask.Result.Error }
                .OfType<string>().Distinct().ToArray();
            ShowError(errors.Length == 0 ? null : string.Join(Environment.NewLine, errors));
        }
        catch (Exception ex)
        {
            ShowError($"No se pudieron cargar los datos: {ex.Message}");
        }
    }

    private static async Task<ApiResult<T>> GetAsync<T>(HttpClient http, string url)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return new ApiResult<T>(await response.Content.ReadFromJsonAsync<T>(Json), null);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.ServiceUnavailable => new ApiResult<T>(default, Unavailable),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ApiResult<T>(default, "Inicia sesión con conexión al servidor (CrewApi:Mode = Live) para ver los datos de Samsung Health."),
                _ => new ApiResult<T>(default, $"Error {(int)response.StatusCode}: {await ReadErrorAsync(response)}"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ApiResult<T>(default, $"No se pudo conectar con el backend ({ex.Message})");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() ?? "" : response.ReasonPhrase ?? "";
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "";
        }
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowSummary(DailySummaryDto? summary)
    {
        LatestValuesDto latest = summary?.Latest ?? new LatestValuesDto();
        CardsPanel.Children.Clear();
        AddCard("Frecuencia cardíaca", latest.HeartRate, v => $"{v.Value:0} {v.Unit ?? "lpm"}");
        AddCard("SpO₂", latest.Spo2, v => $"{v.Value:0} {v.Unit ?? "%"}");
        AddCard("Presión arterial", latest.BloodPressure, v => $"{v.Value:0}/{v.Value2?.ToString("0", CultureInfo.CurrentCulture) ?? "?"} {v.Unit ?? "mmHg"}");
        AddCard("Temperatura de piel", latest.SkinTemperature, v => $"{v.Value:0.0} {v.Unit ?? "°C"}");
        AddCard("Peso", latest.WeightKg, v => $"{v.Value:0.0} {v.Unit ?? "kg"}");
        AddCard("Energy Score", latest.EnergyScore, v => $"{v.Value:0}");
    }

    private void AddCard(string title, LatestValueDto? value, Func<LatestValueDto, string> format)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = value is null ? NoData : format(value),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = value is null ? " " : $"Medido: {value.MeasuredAt.ToLocalTime():g}",
            FontSize = 12,
        });

        var card = new Border
        {
            Child = panel,
            Width = 190,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        CardsPanel.Children.Add(card);
    }

    private void ShowActivity(DailySummaryDto? summary, List<DailyActivityDto>? days)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        int? stepsToday = summary?.Activity?.Steps ?? days?.FirstOrDefault(d => d.LocalDate == today)?.Steps;
        StepsTodayText.Text = $"Pasos de hoy: {(stepsToday is { } s ? s.ToString("N0", CultureInfo.CurrentCulture) : NoData)}";

        ActivityGrid.ItemsSource = (days ?? new List<DailyActivityDto>())
            .OrderByDescending(d => d.LocalDate)
            .Take(7)
            .Select(d => new ActivityRow(
                d.LocalDate.ToString("ddd dd/MM", CultureInfo.CurrentCulture),
                d.Steps?.ToString("N0", CultureInfo.CurrentCulture) ?? "—",
                d.DistanceM is { } m ? $"{m / 1000:0.00} km" : "—",
                d.ActiveCaloriesKcal is { } kcal ? $"{kcal:0} kcal" : "—",
                d.ActiveMinutes is { } min ? $"{min} min" : "—"))
            .ToList();
    }

    private void ShowSleep(DailySummaryDto? summary, List<SleepRecordDto>? nights)
    {
        SleepRecordDto? night = summary?.Sleep ?? nights?.OrderByDescending(n => n.EndAt).FirstOrDefault();
        if (night is null)
        {
            SleepText.Text = NoData;
            SleepStagesText.Visibility = Visibility.Collapsed;
            return;
        }

        SleepText.Text = $"{night.DurationMin / 60} h {night.DurationMin % 60} min  ·  {night.StartAt.ToLocalTime():g} → {night.EndAt.ToLocalTime():t}";
        if (night.StageMinutes is { } st)
        {
            SleepStagesText.Text = $"Despierto {st.Awake} min · Ligero {st.Light} min · Profundo {st.Deep} min · REM {st.Rem} min";
            SleepStagesText.Visibility = Visibility.Visible;
        }
        else
        {
            SleepStagesText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSync(List<SyncStatusDto>? status)
    {
        if (status is not { Count: > 0 })
        {
            SyncText.Text = "Última sincronización de Samsung Health: sin datos";
            return;
        }

        DateTimeOffset last = status.Max(s => s.LastSyncedAt);
        int minutes = Math.Max(0, (int)(DateTimeOffset.Now - last).TotalMinutes);
        SyncText.Text = $"Última sincronización de Samsung Health: hace {minutes} min";
    }
}
