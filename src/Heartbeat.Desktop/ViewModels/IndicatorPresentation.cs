using System.Globalization;

using Heartbeat.Shared;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Cómo se muestra cada indicador: su id en el contrato, su icono y el formato de sus valores.</summary>
internal static class IndicatorPresentation
{
    /// <summary>Id del indicador en <c>reference-ranges.json</c> (por ejemplo <c>heartRate</c>).</summary>
    public static string IdOf(IndicatorType type)
    {
        string name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Glifo de Segoe MDL2 Assets.</summary>
    public static string GlyphOf(IndicatorType type) => type switch
    {
        IndicatorType.BloodPressure => "",
        IndicatorType.HeartRate => "",
        IndicatorType.BloodSugar => "",
        IndicatorType.BodyTemperature => "",
        IndicatorType.Sleep => "",
        IndicatorType.Mood => "",
        _ => "",
    };

    /// <summary>Unidad corta en el idioma activo (por ejemplo «bpm» o «lpm»).</summary>
    public static string UnitOf(IndicatorType type) => type switch
    {
        IndicatorType.BloodPressure => "mmHg",
        IndicatorType.HeartRate => Loc.T("unit.heartRate"),
        IndicatorType.BloodSugar => "mg/dL",
        IndicatorType.BodyTemperature => "°C",
        IndicatorType.Sleep => "h",
        IndicatorType.Mood => "/ 5",
        _ => string.Empty,
    };

    /// <summary>Solo el número, sin unidad (por ejemplo «120/78»).</summary>
    public static string FormatNumber(ReadingInput reading)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        return reading.Indicator switch
        {
            IndicatorType.BloodPressure => string.Create(culture, $"{reading.Value:0}/{reading.SecondaryValue:0}"),
            IndicatorType.BodyTemperature => string.Create(culture, $"{reading.Value:0.0}"),
            IndicatorType.Sleep => string.Create(culture, $"{reading.Value:0.#}"),
            _ => string.Create(culture, $"{reading.Value:0}"),
        };
    }

    /// <summary>Número y unidad juntos (por ejemplo «120/78 mmHg»).</summary>
    public static string FormatValue(ReadingInput reading) => $"{FormatNumber(reading)} {UnitOf(reading.Indicator)}";

    /// <summary>Cuándo se midió, en lenguaje cercano: «Hoy, 19:45», «Ayer, 19:45», «Hace 3 días, 19:45» o la fecha completa.</summary>
    public static string FormatWhen(DateTimeOffset at, TimeProvider clock)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        string time = local.ToString("t", culture);
        int days = (clock.GetLocalNow().Date - local.Date).Days;
        return days switch
        {
            <= 0 => Loc.T("when.today", time),
            1 => Loc.T("when.yesterday", time),
            < 7 => Loc.T("when.daysAgo", days, time),
            _ => local.ToString("g", culture),
        };
    }

    /// <summary>Resumen de la guía de una lectura: el texto de la guía, o una frase estándar si está en rango.</summary>
    public static string GuidanceOf(ReadingEvaluation evaluation) =>
        evaluation.Status == HealthStatus.Normal
            ? Loc.T("result.inRange")
            : string.Join(" ", evaluation.Guidance);
}
