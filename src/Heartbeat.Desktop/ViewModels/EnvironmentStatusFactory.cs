using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Evalúa una lectura guardada en el equipo con las bandas del catálogo compartido (nunca con umbrales propios).</summary>
internal static class EnvironmentStatusFactory
{
    public static EnvironmentReadingStatus? From(EnvironmentReadingRecord record)
    {
        IndicatorCatalogEntry? entry = IndicatorsCatalog.Find(record.IndicatorId);
        if (entry is null)
        {
            return null;
        }

        IndicatorBand band = IndicatorsCatalog.Evaluate(entry, record.Value);
        return new EnvironmentReadingStatus(
            record.Id, record.RecordedAt, record.ReceivedAt, entry.Id, entry.Name, record.Value, record.SecondaryValue,
            entry.Unit, band.Status, band.Label, entry.Status, IndicatorsCatalog.SourceFor(entry), null);
    }
}
