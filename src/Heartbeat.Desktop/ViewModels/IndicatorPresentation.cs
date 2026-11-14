using System.Globalization;

using Heartbeat.Shared;

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

    public static string FormatValue(ReadingInput reading)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        return reading.Indicator switch
        {
            IndicatorType.BloodPressure => string.Create(culture, $"{reading.Value:0}/{reading.SecondaryValue:0} mmHg"),
            IndicatorType.HeartRate => string.Create(culture, $"{reading.Value:0} lpm"),
            IndicatorType.BloodSugar => string.Create(culture, $"{reading.Value:0} mg/dL"),
            IndicatorType.BodyTemperature => string.Create(culture, $"{reading.Value:0.0} °C"),
            IndicatorType.Sleep => string.Create(culture, $"{reading.Value:0.#} h"),
            IndicatorType.Mood => string.Create(culture, $"{reading.Value:0} / 5"),
            _ => string.Create(culture, $"{reading.Value}"),
        };
    }

    /// <summary>Resumen de la guía de una lectura: el texto de la guía, o una frase estándar si está en rango.</summary>
    public static string GuidanceOf(ReadingEvaluation evaluation) =>
        evaluation.Status == HealthStatus.Normal
            ? "Dentro del rango de referencia."
            : string.Join(" ", evaluation.Guidance);
}
