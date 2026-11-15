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

        AddMissingColumns(db);
    }

    // EnsureCreated no altera tablas que ya existen: las bases creadas antes de #9 no tienen Source ni DeviceId.
    private static void AddMissingColumns(LocalDbContext db)
    {
        AddColumnIfMissing(db, "Readings", "Source", "ALTER TABLE Readings ADD COLUMN Source TEXT NOT NULL DEFAULT 'manual'");
        AddColumnIfMissing(db, "Readings", "DeviceId", "ALTER TABLE Readings ADD COLUMN DeviceId TEXT NULL");
    }

    private static void AddColumnIfMissing(LocalDbContext db, string table, string column, string alterSql)
    {
        bool exists = db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info({table}) WHERE name = {column}")
            .AsEnumerable()
            .Single() > 0;

        if (!exists)
        {
            db.Database.ExecuteSqlRaw(alterSql);
        }
    }
}
