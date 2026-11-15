using Heartbeat.Desktop.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Data;

/// <summary>Almacén SQLite de las últimas lecturas de ambiente recibidas o simuladas.</summary>
public sealed class LocalEnvironmentReadingStore(IServiceScopeFactory scopes) : IEnvironmentReadingStore
{
    public IReadOnlyList<EnvironmentReadingRecord> GetLatest(Guid astronautId)
    {
        using IServiceScope scope = scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        return db.EnvironmentReadings.AsNoTracking()
            .Where(r => r.AstronautId == astronautId)
            .ToList()
            .OrderByDescending(r => r.RecordedAt)
            .GroupBy(r => r.IndicatorId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(r => new EnvironmentReadingRecord(
                r.Id,
                r.IndicatorId,
                r.Value,
                r.SecondaryValue,
                r.Unit,
                r.RecordedAt,
                r.ReceivedAt,
                r.Simulated))
            .ToList();
    }
}
