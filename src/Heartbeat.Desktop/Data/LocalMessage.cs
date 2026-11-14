using Heartbeat.Shared;

namespace Heartbeat.Desktop.Data;

public class LocalMessage
{
    public Guid Id { get; set; }
    public Guid AstronautId { get; set; }
    public MessageDirection Direction { get; set; }
    public MessageKind Kind { get; set; }
    public Guid? CheckUpId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public Guid? SenderUserId { get; set; }
    public SyncState SyncState { get; set; } = SyncState.Pending;
    public string? SyncError { get; set; }
}