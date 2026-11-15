namespace Heartbeat.Desktop.Data;

public class LocalRadiationDose
{
    public Guid Id { get; set; }
    public Guid AstronautId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public decimal DoseMsv { get; set; }
    public string? Note { get; set; }
    public SyncState SyncState { get; set; } = SyncState.Pending;
    public string? SyncError { get; set; }
}
