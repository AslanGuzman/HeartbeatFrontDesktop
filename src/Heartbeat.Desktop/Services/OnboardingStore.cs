using System.IO;
using System.Text.Json;

using Heartbeat.Desktop.Data;

namespace Heartbeat.Desktop.Services;

/// <summary>Recuerda qué astronautas ya vieron la introducción de 3 pasos para no repetirla (HB-FE-01).</summary>
public interface IOnboardingStore
{
    bool IsCompleted(Guid astronautId);

    void MarkCompleted(Guid astronautId);
}

/// <summary>
/// Archivo JSON junto a la base local con los ids que ya completaron la introducción. Si falta o está dañado se
/// considera que nadie la vio (se vuelve a mostrar, que es lo seguro).
/// </summary>
public sealed class FileOnboardingStore : IOnboardingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileOnboardingStore()
        : this(Path.Combine(Path.GetDirectoryName(LocalDatabase.DefaultPath)!, "onboarding.json"))
    {
    }

    public FileOnboardingStore(string path) => _path = path;

    public bool IsCompleted(Guid astronautId)
    {
        lock (_gate)
        {
            return Read().Contains(astronautId);
        }
    }

    public void MarkCompleted(Guid astronautId)
    {
        lock (_gate)
        {
            HashSet<Guid> completed = Read();
            if (!completed.Add(astronautId))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                File.WriteAllText(_path, JsonSerializer.Serialize(completed, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sin disco la introducción volverá a aparecer; no es motivo para romper el panel.
            }
        }
    }

    private HashSet<Guid> Read()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<HashSet<Guid>>(File.ReadAllText(_path), JsonOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
