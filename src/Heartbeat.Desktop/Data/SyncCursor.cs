namespace Heartbeat.Desktop.Data;

public class SyncCursor
{
    public Guid AstronautId { get; set; }
    public DateTimeOffset? LastPulledAt { get; set; }
}