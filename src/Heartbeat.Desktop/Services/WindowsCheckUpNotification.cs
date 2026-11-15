using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Heartbeat.Desktop.Services;

/// <summary>Aviso de Windows de que toca el check-up de rutina.</summary>
public interface ICheckUpNotification
{
    void Show();
}

/// <summary>
/// Burbuja del área de notificación (Windows 10 y 11 la muestran como notificación). El icono se crea en el hilo de
/// la interfaz la primera vez que hace falta.
/// </summary>
public sealed class WindowsCheckUpNotification : ICheckUpNotification, IDisposable
{
    private Forms.NotifyIcon? _icon;

    public void Show()
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is not null)
        {
            app.Dispatcher.Invoke(ShowCore);
        }
    }

    public void Dispose()
    {
        Forms.NotifyIcon? icon = _icon;
        if (icon is null)
        {
            return;
        }

        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is not null)
        {
            app.Dispatcher.Invoke(() =>
            {
                icon.Visible = false;
                icon.Dispose();
            });
        }
    }

    private void ShowCore()
    {
        Forms.NotifyIcon icon = _icon ??= new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Information,
            Text = AppText.AppName,
            Visible = true,
        };

        icon.ShowBalloonTip(
            10_000,
            "Check-up pendiente",
            "Ya toca tu check-up de rutina. Regístralo en Heartbeat.",
            Forms.ToolTipIcon.Info);
    }
}
