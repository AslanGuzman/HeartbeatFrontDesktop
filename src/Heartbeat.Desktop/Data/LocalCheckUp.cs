using Heartbeat.Shared;

namespace Heartbeat.Desktop.Data;

public class LocalCheckUp
{
    public Guid Id { get; set; }
    public Guid AstronautId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public HealthStatus OverallStatus { get; set; }
    public string RangesVersion { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid? SupersedesId { get; set; }
    public SyncState SyncState { get; set; } = SyncState.Pending;
    public string? SyncError { get; set; }

    public ICollection<LocalReading> Readings { get; set; } = new List<LocalReading>();
}