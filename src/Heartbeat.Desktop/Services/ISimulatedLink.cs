namespace Heartbeat.Desktop.Services;

/// <summary>
/// Interruptor "Enlace con la Tierra". Permite demostrar el modo offline sin desconectar el equipo: con el enlace
/// apagado la app se comporta como sin conexión aunque haya red.
/// </summary>
public interface ISimulatedLink
{
    bool IsLinkEnabled { get; set; }
}
