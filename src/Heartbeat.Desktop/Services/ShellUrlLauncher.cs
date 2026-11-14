using System.ComponentModel;
using System.Diagnostics;

namespace Heartbeat.Desktop.Services;

/// <summary>Abre enlaces http(s) con el navegador predeterminado de Windows.</summary>
public sealed class ShellUrlLauncher : IUrlLauncher
{
    public bool TryOpen(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Solo http(s): con UseShellExecute cualquier otro esquema (file:, ms-settings:...) ejecutaría otra cosa.
        if (!url.IsAbsoluteUri || url.Scheme is not ("https" or "http"))
        {
            return false;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Sin navegador predeterminado o bloqueado por política: el ViewModel lo explica al usuario.
            return false;
        }
    }
}
