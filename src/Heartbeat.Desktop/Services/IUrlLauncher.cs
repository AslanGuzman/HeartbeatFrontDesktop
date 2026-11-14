namespace Heartbeat.Desktop.Services;

/// <summary>Abre un enlace en el navegador del sistema.</summary>
public interface IUrlLauncher
{
    /// <returns><c>true</c> si el sistema aceptó abrir el enlace.</returns>
    bool TryOpen(Uri url);
}
