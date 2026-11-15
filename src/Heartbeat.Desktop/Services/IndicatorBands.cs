using System.Globalization;
using System.Text.Json;

using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services;

/// <summary>
/// Un tramo de valores con un estado (NR-01): <c>From</c> incluido, <c>To</c> excluido, como en <c>HealthEvaluator</c>.
/// Un extremo <c>null</c> está abierto.
/// </summary>
public sealed record BandSpan(HealthStatus Status, double? From, double? To)
{
    public bool Contains(double value) => (From is null || value >= From) && (To is null || value < To);
}

/// <summary>
/// Lo que se puede dibujar en una gráfica de tendencia: un valor de un indicador con sus tramos de color. La presión
/// arterial son dos (sistólica y diastólica), cada una con sus propios tramos.
/// </summary>
public sealed record ChartMetric(
    string Key,
    IndicatorType Indicator,
    bool IsSecondary,
    IReadOnlyList<BandSpan> Bands,
    double ValidMin,
    double ValidMax)
{
    public double? ValueOf(ReadingInput reading) => IsSecondary ? (double?)reading.SecondaryValue : (double)reading.Value;

    /// <summary>Estado de un valor según los tramos; <see cref="HealthStatus.Normal"/> si ninguno lo contiene.</summary>
    public HealthStatus StatusOf(double value) => Bands.FirstOrDefault(b => b.Contains(value))?.Status ?? HealthStatus.Normal;
}

/// <summary>
/// Los tramos de color de cada indicador, leídos de <c>reference-ranges.json</c> (el mismo fichero y la misma versión
/// que usa la evaluación), para sombrear las gráficas con las bandas del contrato.
/// </summary>
public sealed class IndicatorBands
{
    private IndicatorBands(IReadOnlyList<ChartMetric> metrics) => Metrics = metrics;

    /// <summary>Métricas en el orden del contrato (presión sistólica y diastólica, frecuencia, glucosa…).</summary>
    public IReadOnlyList<ChartMetric> Metrics { get; }

    public ChartMetric? Find(string key) => Metrics.FirstOrDefault(m => m.Key == key);

    public static IndicatorBands Parse(string rangesJson)
    {
        using JsonDocument doc = JsonDocument.Parse(rangesJson);
        List<ChartMetric> metrics = [];

        foreach (JsonElement indicator in doc.RootElement.GetProperty("indicators").EnumerateArray())
        {
            string id = indicator.GetProperty("id").GetString()!;
            if (!Enum.TryParse(char.ToUpperInvariant(id[0]) + id[1..], out IndicatorType type))
            {
                continue;
            }

            JsonElement valid = indicator.GetProperty("validRange");
            if (indicator.TryGetProperty("bands", out JsonElement bands))
            {
                metrics.Add(Create(id, type, false, bands, valid.GetProperty("value")));
            }
            else if (indicator.TryGetProperty("components", out JsonElement components))
            {
                metrics.Add(Create(id + ".value", type, false, components.GetProperty("value"), valid.GetProperty("value")));
                if (components.TryGetProperty("secondaryValue", out JsonElement secondary))
                {
                    metrics.Add(Create(id + ".secondaryValue", type, true, secondary, valid.GetProperty("secondaryValue")));
                }
            }
        }

        return new IndicatorBands(metrics);
    }

    /// <summary>Clave de la métrica de un valor: <c>heartRate</c>, <c>bloodPressure.value</c>…</summary>
    public static string KeyOf(IndicatorType type, bool secondary)
    {
        string id = char.ToLowerInvariant(type.ToString()[0]) + type.ToString()[1..];
        return type == IndicatorType.BloodPressure ? id + (secondary ? ".secondaryValue" : ".value") : id;
    }

    private static ChartMetric Create(string key, IndicatorType type, bool secondary, JsonElement bands, JsonElement valid)
    {
        List<BandSpan> spans = [];
        foreach (JsonElement band in bands.EnumerateArray())
        {
            HealthStatus status = band.GetProperty("status").GetString() switch
            {
                "critical" => HealthStatus.Critical,
                "watch" => HealthStatus.Watch,
                _ => HealthStatus.Normal,
            };
            spans.Add(new BandSpan(status, Number(band, "min"), Number(band, "max")));
        }

        return new ChartMetric(key, type, secondary, spans, Number(valid, "min") ?? 0, Number(valid, "max") ?? double.MaxValue);
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
