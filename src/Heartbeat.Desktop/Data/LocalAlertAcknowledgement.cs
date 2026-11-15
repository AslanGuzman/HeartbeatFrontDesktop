namespace Heartbeat.Desktop.Data;

/// <summary>
/// Un check-up crítico cuya alerta reconoció control de misión. Llega por sync (<c>inbound.acknowledgedAlerts</c>); la
/// clave es el check-up, así que repetirla no duplica nada.
/// </summary>
public class LocalAlertAcknowledgement
{
    public Guid CheckUpId { get; set; }
    public Guid AstronautId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
}
