using System.Globalization;
using System.Text;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Compone el asunto y el cuerpo de un reporte que adjunta un check-up (MR-11). Lo usan tanto el resultado de un
/// check-up («Enviar reporte») como la pantalla de mensajes, para que el texto sea el mismo en los dos sitios.
/// </summary>
public static class ReportBuilder
{
    public static string Subject(CheckUpRecord checkUp)
    {
        ArgumentNullException.ThrowIfNull(checkUp);
        return $"Check-up {checkUp.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {StatusPalette.Name(checkUp.OverallStatus)}";
    }

    public static string Body(CheckUpRecord checkUp, IIndicatorInfoProvider info)
    {
        ArgumentNullException.ThrowIfNull(checkUp);
        ArgumentNullException.ThrowIfNull(info);

        StringBuilder text = new();
        text.Append(CultureInfo.CurrentCulture, $"Estado general: {StatusPalette.Name(checkUp.OverallStatus)}.");
        foreach (ReadingRecord reading in checkUp.Readings)
        {
            string id = IndicatorPresentation.IdOf(reading.Input.Indicator);
            string name = info.Find(id)?.Name ?? id;
            text.Append(CultureInfo.CurrentCulture,
                $"\n- {name}: {IndicatorPresentation.FormatValue(reading.Input)} — {StatusPalette.Name(reading.Evaluation.Status)} ({reading.Evaluation.Label})");
        }

        if (!string.IsNullOrWhiteSpace(checkUp.Note))
        {
            text.Append("\nNota: ").Append(checkUp.Note);
        }

        return text.ToString();
    }
}
