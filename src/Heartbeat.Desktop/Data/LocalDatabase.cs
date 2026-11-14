using System.IO;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Data;

public static class LocalDatabase
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Heartbeat",
        "heartbeat-desktop.db");

    public static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    public static void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        string dataSource = db.Database.GetDbConnection().DataSource;
        string? directory = string.IsNullOrEmpty(dataSource) ? null : Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // No hay migraciones: la base local es una cache del servidor. Si el esquema cambia, hay que introducir
        // migraciones (o recrear la base) antes de publicar una version con datos pendientes de sync.
        db.Database.EnsureCreated();
    }
}
