namespace Heartbeat.Desktop.Data;

/// <summary>Última lectura de ambiente guardada localmente para un astronauta.</summary>
public sealed class LocalEnvironmentReading
{
    public Guid Id { get; set; }
    public Guid AstronautId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string IndicatorId { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool Simulated { get; set; } = true;
}
