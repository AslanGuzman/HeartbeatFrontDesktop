using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>El contenedor real de la app, pero con una base SQLite temporal ya creada.</summary>
internal static class TestContainer
{
    public static ServiceCollection Services(out TestDatabase database)
    {
        database = new TestDatabase();
        string connectionString = database.ConnectionString;

        ServiceCollection services = new();
        services.AddHeartbeatDesktop();

        // El id del equipo no se escribe en %LOCALAPPDATA% durante las pruebas.
        services.AddSingleton<IDeviceIdProvider>(new FixedDeviceId("equipo-de-pruebas"));

        // El recordatorio no toca el archivo ni el área de notificación reales.
        services.AddSingleton<ICheckUpReminderSettingsStore>(new FakeReminderSettings(CheckUpReminderSettings.Disabled));
        services.AddSingleton<ICheckUpNotification>(new FakeCheckUpNotification());

        // El último registro gana: se sustituye la base de %LOCALAPPDATA% por la temporal.
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite(connectionString));

        return services;
    }
}

internal sealed class FixedDeviceId(string id) : IDeviceIdProvider
{
    public string DeviceId { get; } = id;
}
