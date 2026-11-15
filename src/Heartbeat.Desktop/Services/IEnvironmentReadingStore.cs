namespace Heartbeat.Desktop.Services;

/// <summary>Una lectura de ambiente persistida en el equipo.</summary>
public sealed record EnvironmentReadingRecord(
    Guid Id,
    string IndicatorId,
    decimal Value,
    decimal? SecondaryValue,
    string Unit,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ReceivedAt,
    bool Simulated);

/// <summary>Lee las últimas lecturas de ambiente disponibles sin depender del enlace.</summary>
public interface IEnvironmentReadingStore
{
    IReadOnlyList<EnvironmentReadingRecord> GetLatest(Guid astronautId);
}
