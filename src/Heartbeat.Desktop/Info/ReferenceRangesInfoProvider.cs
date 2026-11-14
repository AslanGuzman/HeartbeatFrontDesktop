using System.IO;
using System.Text.Json;

using Heartbeat.Shared;

namespace Heartbeat.Desktop.Info;

/// <summary>
/// Lee las fichas de <c>reference-ranges.json</c>. Por defecto usa la copia embebida en <c>Heartbeat.Shared</c>;
/// cuando exista la caché de rangos del servidor (ticket #8 / #32) bastará con pasarle ese JSON.
/// </summary>
/// <remarks>
/// El análisis es estricto a propósito: si el contrato cambia de forma o cita una fuente que no define, se prefiere
/// un error claro (y una prueba en rojo) a mostrar una ficha incompleta al astronauta.
/// </remarks>
public sealed class ReferenceRangesInfoProvider : IIndicatorInfoProvider
{
    // Los cinco peligros de la salud humana en el espacio según NASA HRP (campo "hazard" del contrato).
    private static readonly Dictionary<string, string> HazardNames = new(StringComparer.Ordinal)
    {
        ["spaceRadiation"] = "Radiación espacial",
        ["isolationAndConfinement"] = "Aislamiento y confinamiento",
        ["distanceFromEarth"] = "Distancia de la Tierra",
        ["gravityFields"] = "Campos de gravedad",
        ["hostileClosedEnvironment"] = "Entorno cerrado y hostil",
    };

    // Nombres en español mientras el contrato esté en inglés (AliaJimenez/HeartbeatBackend#79). Si el contrato
    // ya trae el nombre traducido, borrar este diccionario y usar directamente el campo "name".
    private static readonly Dictionary<string, string> SpanishNames = new(StringComparer.Ordinal)
    {
        ["bloodPressure"] = "Presión arterial",
        ["heartRate"] = "Frecuencia cardíaca",
        ["bloodSugar"] = "Glucosa",
        ["bodyTemperature"] = "Temperatura corporal",
        ["sleep"] = "Sueño",
        ["mood"] = "Estado de ánimo",
    };

    private readonly Dictionary<string, IndicatorInfo> _byId;

    public ReferenceRangesInfoProvider()
        : this(ReferenceRanges.Json)
    {
    }

    public ReferenceRangesInfoProvider(string rangesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rangesJson);

        using JsonDocument document = JsonDocument.Parse(rangesJson);
        JsonElement root = document.RootElement;

        RangesVersion = RequiredString(root, "version", "raíz");
        Dictionary<string, InfoSource> sources = ReadSources(root);

        List<IndicatorInfo> indicators = [];
        foreach (JsonElement indicator in Required(root, "indicators", "raíz").EnumerateArray())
        {
            indicators.Add(ReadIndicator(indicator, sources));
        }

        if (indicators.Count == 0)
        {
            throw new InvalidDataException("reference-ranges.json no define ningún indicador.");
        }

        Indicators = indicators;
        _byId = indicators.ToDictionary(i => i.Id, StringComparer.Ordinal);
    }

    public string RangesVersion { get; }

    public IReadOnlyList<IndicatorInfo> Indicators { get; }

    public IndicatorInfo? Find(string id) => id is not null && _byId.TryGetValue(id, out IndicatorInfo? info) ? info : null;

    private static Dictionary<string, InfoSource> ReadSources(JsonElement root)
    {
        Dictionary<string, InfoSource> sources = new(StringComparer.Ordinal);
        foreach (JsonProperty property in Required(root, "sources", "raíz").EnumerateObject())
        {
            string title = RequiredString(property.Value, "title", $"fuente '{property.Name}'");
            string url = RequiredString(property.Value, "url", $"fuente '{property.Name}'");

            // Solo http(s): estos enlaces se abren con el navegador del sistema.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "http"))
            {
                throw new InvalidDataException($"La fuente '{property.Name}' tiene una URL no válida: '{url}'.");
            }

            sources.Add(property.Name, new InfoSource(title, uri));
        }

        return sources;
    }

    private static IndicatorInfo ReadIndicator(JsonElement indicator, Dictionary<string, InfoSource> sources)
    {
        string id = RequiredString(indicator, "id", "indicador");
        string where = $"indicador '{id}'";

        string hazardId = RequiredString(indicator, "hazard", where);
        string hazardName = HazardNames.GetValueOrDefault(hazardId) ?? hazardId;

        List<string> howToMeasure = Required(indicator, "howToMeasure", where)
            .EnumerateArray()
            .Select(step => step.GetString() ?? throw new InvalidDataException($"{where}: un paso de 'howToMeasure' no es texto."))
            .ToList();

        List<InfoSource> indicatorSources = [];
        foreach (JsonElement key in Required(indicator, "sources", where).EnumerateArray())
        {
            string sourceId = key.GetString() ?? string.Empty;
            indicatorSources.Add(
                sources.GetValueOrDefault(sourceId)
                ?? throw new InvalidDataException($"{where} cita la fuente '{sourceId}', que no está definida en 'sources'."));
        }

        if (howToMeasure.Count == 0 || indicatorSources.Count == 0)
        {
            throw new InvalidDataException($"{where} debe tener al menos un paso de medición y una fuente.");
        }

        return new IndicatorInfo(
            id,
            SpanishNames.GetValueOrDefault(id) ?? RequiredString(indicator, "name", where),
            RequiredString(indicator, "unit", where),
            hazardName,
            RequiredString(indicator, "whyItMatters", where),
            howToMeasure,
            RequiredString(indicator, "risks", where),
            indicatorSources);
    }

    private static JsonElement Required(JsonElement element, string property, string where)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            throw new InvalidDataException($"reference-ranges.json: falta '{property}' en {where}.");
        }

        return value;
    }

    private static string RequiredString(JsonElement element, string property, string where)
    {
        string? value = Required(element, property, where).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"reference-ranges.json: '{property}' está vacío en {where}.")
            : value;
    }
}
