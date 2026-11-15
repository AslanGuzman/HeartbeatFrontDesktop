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
        AddMissingTables(db);
    }

    // EnsureCreated tampoco crea tablas nuevas en una base que ya existe: las bases anteriores a #51 no tienen RadiationDoses.
    // El SQL replica lo que EF crea para una base nueva.
    private static void AddMissingTables(LocalDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS RadiationDoses (" +
            "Id TEXT NOT NULL CONSTRAINT PK_RadiationDoses PRIMARY KEY, " +
            "AstronautId TEXT NOT NULL, " +
            "RecordedAt TEXT NOT NULL, " +
            "ReceivedAt TEXT NULL, " +
            "DoseMsv TEXT NOT NULL, " +
            "Note TEXT NULL, " +
            "SyncState TEXT NOT NULL, " +
            "SyncError TEXT NULL)");
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_RadiationDoses_AstronautId_RecordedAt ON RadiationDoses (AstronautId, RecordedAt)");
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_RadiationDoses_AstronautId_SyncState ON RadiationDoses (AstronautId, SyncState)");
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS EnvironmentReadings (" +
            "Id TEXT NOT NULL CONSTRAINT PK_EnvironmentReadings PRIMARY KEY, " +
            "AstronautId TEXT NOT NULL, " +
            "RecordedAt TEXT NOT NULL, " +
            "ReceivedAt TEXT NULL, " +
            "IndicatorId TEXT NOT NULL, " +
            "Value TEXT NOT NULL, " +
            "SecondaryValue TEXT NULL, " +
            "Unit TEXT NOT NULL, " +
            "Simulated INTEGER NOT NULL DEFAULT 1)");
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_EnvironmentReadings_AstronautId_IndicatorId_RecordedAt ON EnvironmentReadings (AstronautId, IndicatorId, RecordedAt)");

        // Alertas reconocidas por control de misión (#33).
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS AlertAcknowledgements (" +
            "CheckUpId TEXT NOT NULL CONSTRAINT PK_AlertAcknowledgements PRIMARY KEY, " +
            "AstronautId TEXT NOT NULL, " +
            "AcknowledgedAt TEXT NOT NULL, " +
            "AcknowledgedBy TEXT NOT NULL, " +
            "Note TEXT NULL)");
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_AlertAcknowledgements_AstronautId_AcknowledgedAt ON AlertAcknowledgements (AstronautId, AcknowledgedAt)");
    }

    // EnsureCreated no altera tablas que ya existen: las bases creadas antes de #9 no tienen Source ni DeviceId.
    private static void AddMissingColumns(LocalDbContext db)
    {
        AddColumnIfMissing(db, "Readings", "Source", "ALTER TABLE Readings ADD COLUMN Source TEXT NOT NULL DEFAULT 'manual'");
        AddColumnIfMissing(db, "Readings", "DeviceId", "ALTER TABLE Readings ADD COLUMN DeviceId TEXT NULL");
        AddColumnIfMissing(db, "Messages", "SenderName", "ALTER TABLE Messages ADD COLUMN SenderName TEXT NULL");
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
