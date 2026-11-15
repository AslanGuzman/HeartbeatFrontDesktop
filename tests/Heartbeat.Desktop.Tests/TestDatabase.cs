using System.IO;

using Heartbeat.Desktop.Data;

using Microsoft.EntityFrameworkCore;

namespace Heartbeat.Desktop.Tests;

/// <summary>
/// Base SQLite en un archivo temporal, como la de la app: cada contexto abre su propia conexión, de modo que el hilo de
/// la prueba y el del motor de sincronización pueden usarla a la vez. (Una conexión en memoria compartida entre hilos
/// no es válida en SQLite.) Se borra al terminar.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "heartbeat-test-" + Guid.NewGuid().ToString("N") + ".db");

    public TestDatabase()
    {
        ConnectionString = LocalDatabase.ConnectionString(_path) + ";Pooling=False";
        DbContextOptions<LocalDbContext> options = new DbContextOptionsBuilder<LocalDbContext>().UseSqlite(ConnectionString).Options;
        using LocalDbContext db = new(options);
        db.Database.EnsureCreated();
    }

    public string ConnectionString { get; }

    public void Dispose()
    {
        foreach (string file in new[] { _path, _path + "-journal", _path + "-wal", _path + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
