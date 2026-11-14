namespace Heartbeat.Desktop.Data;

public class LocalAccount
{
    public Guid AstronautId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public DateTimeOffset? LastOnlineLoginAt { get; set; }
    public string? ProfileJson { get; set; }
}