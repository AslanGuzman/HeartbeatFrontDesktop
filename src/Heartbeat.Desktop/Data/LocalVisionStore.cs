using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Data;

/// <summary>Guarda las lecturas de visión en la misma tabla local de lecturas de ambiente.</summary>
public sealed class LocalVisionStore(IServiceScopeFactory scopes, TimeProvider clock) : IVisionStore
{
    private static readonly string[] Tracked = [VisionAnalysis.IopId, VisionAnalysis.VisualAcuityId, VisionAnalysis.Co2Id];

    public IReadOnlyList<EnvironmentReadingRecord> GetReadings(Guid astronautId, DateTimeOffset from)
    {
        using IServiceScope scope = scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // SQLite no ordena ni compara DateTimeOffset: se filtra y ordena en memoria.
        return db.EnvironmentReadings.AsNoTracking()
            .Where(r => r.AstronautId == astronautId && Tracked.Contains(r.IndicatorId))
            .ToList()
            .Where(r => r.RecordedAt >= from)
            .OrderBy(r => r.RecordedAt)
            .Select(ToRecord)
            .ToList();
    }

    public EnvironmentReadingRecord Add(Guid astronautId, string indicatorId, decimal value)
    {
        if (!VisionAnalysis.IsVisionIndicator(indicatorId))
        {
            throw new ArgumentException("Solo se registran presión ocular y agudeza visual.", nameof(indicatorId));
        }

        IndicatorCatalogEntry entry = IndicatorsCatalog.Find(indicatorId)
            ?? throw new ArgumentException($"El catálogo no tiene '{indicatorId}'.", nameof(indicatorId));

        using IServiceScope scope = scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        LocalEnvironmentReading reading = new()
        {
            Id = Guid.CreateVersion7(),
            AstronautId = astronautId,
            RecordedAt = clock.GetUtcNow(),
            IndicatorId = entry.Id,
            Value = value,
            Unit = entry.Unit,
            Simulated = true,
        };
        db.EnvironmentReadings.Add(reading);
        db.SaveChanges();
        return ToRecord(reading);
    }

    private static EnvironmentReadingRecord ToRecord(LocalEnvironmentReading r) => new(
        r.Id, r.IndicatorId, r.Value, r.SecondaryValue, r.Unit, r.RecordedAt, r.ReceivedAt, r.Simulated);
}
