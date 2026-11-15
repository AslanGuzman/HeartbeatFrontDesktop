using System.IO;

using Heartbeat.Desktop.Data;

namespace Heartbeat.Desktop.Services.Auth;

/// <summary>Identificador estable de este equipo, que el servidor recibe en el login (<c>LoginRequest.DeviceId</c>).</summary>
public interface IDeviceIdProvider
{
    string DeviceId { get; }
}

/// <summary>Se genera la primera vez y se guarda junto a la base local; sobrevive a reinicios.</summary>
public sealed class FileDeviceIdProvider : IDeviceIdProvider
{
    private readonly Lazy<string> _id;

    public FileDeviceIdProvider()
        : this(Path.Combine(Path.GetDirectoryName(LocalDatabase.DefaultPath)!, "device-id.txt"))
    {
    }

    public FileDeviceIdProvider(string path)
    {
        _id = new Lazy<string>(() => ReadOrCreate(path));
    }

    public string DeviceId => _id.Value;

    private static string ReadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } existing)
            {
                return existing;
            }

            string id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, id);
            return id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin acceso al disco no se puede recordar: se usa uno para esta ejecución.
            return Guid.NewGuid().ToString("N");
        }
    }
}
