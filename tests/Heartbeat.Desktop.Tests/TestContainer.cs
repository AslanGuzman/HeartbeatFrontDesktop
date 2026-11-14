using Heartbeat.Desktop.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>El contenedor real de la app, pero con la base SQLite en memoria y ya creada.</summary>
internal static class TestContainer
{
    public static ServiceCollection Services(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        ServiceCollection services = new();
        services.AddHeartbeatDesktop();

        // El último registro gana: se sustituye la base de %LOCALAPPDATA% por una en memoria.
        SqliteConnection shared = connection;
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite(shared));
        using (ServiceProvider temp = services.BuildServiceProvider())
        using (IServiceScope scope = temp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<LocalDbContext>().Database.EnsureCreated();
        }

        return services;
    }
}
