using Heartbeat.Shared;

namespace Heartbeat.Desktop.Data;

public class LocalReading
{
    public Guid Id { get; set; }
    public Guid CheckUpId { get; set; }
    public IndicatorType Indicator { get; set; }
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
    public HealthStatus Status { get; set; }
    public string BandIds { get; set; } = string.Empty;
}