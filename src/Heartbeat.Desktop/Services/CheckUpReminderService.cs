using Microsoft.Extensions.Hosting;

namespace Heartbeat.Desktop.Services;

/// <summary>Revisa cada minuto si toca el recordatorio; el host lo arranca y detiene con la app.</summary>
public sealed class CheckUpReminderService(CheckUpReminder reminder) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                reminder.Check();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cierre normal de la app.
        }
    }
}
