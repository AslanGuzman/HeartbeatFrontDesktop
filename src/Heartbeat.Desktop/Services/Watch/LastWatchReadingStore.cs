using System.IO;
using System.Text.Json;

using Heartbeat.Desktop.Live;

namespace Heartbeat.Desktop.Services.Watch;

/// <summary>
/// La última lectura del reloj de cada astronauta, guardada en el equipo: sin enlace (o sin datos del reloj) la pantalla
/// puede enseñar la última FC conocida con su hora, y avisar de que ya no es en vivo.
/// </summary>
public interface ILastWatchReadingStore
{
    HeartRateReading? Get(Guid astronautId);

    void Save(Guid astronautId, HeartRateReading reading);
}

public sealed class FileLastWatchReadingStore : ILastWatchReadingStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly object _gate = new();

    public FileLastWatchReadingStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Heartbeat",
            "watch-last.json"))
    {
    }

    public FileLastWatchReadingStore(string path) => _path = path;

    public HeartRateReading? Get(Guid astronautId)
    {
        lock (_gate)
        {
            return Read().GetValueOrDefault(astronautId.ToString("N"));
        }
    }

    public void Save(Guid astronautId, HeartRateReading reading)
    {
        lock (_gate)
        {
            try
            {
                Dictionary<string, HeartRateReading> all = Read();
                all[astronautId.ToString("N")] = reading;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(all, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Es una comodidad: si no se puede escribir, simplemente no queda guardada.
            }
        }
    }

    private Dictionary<string, HeartRateReading> Read()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, HeartRateReading>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
