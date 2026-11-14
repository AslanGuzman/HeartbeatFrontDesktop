namespace Heartbeat.Desktop.Services;

/// <summary>Informa si el equipo tiene enlace de red. La barra de estado lo muestra como "En línea" / "Sin conexión".</summary>
public interface IConnectivityService
{
    bool IsOnline { get; }

    /// <summary>Se produce cuando cambia <see cref="IsOnline"/>. Puede dispararse en un hilo distinto al de la interfaz.</summary>
    event EventHandler? IsOnlineChanged;
}
