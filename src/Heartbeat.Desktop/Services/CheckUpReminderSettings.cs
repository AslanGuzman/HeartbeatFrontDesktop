using System.IO;
using System.Text.Json;

using Heartbeat.Desktop.Data;

namespace Heartbeat.Desktop.Services;

/// <summary>
/// Horario del recordatorio diario de check-up. Es un ajuste del equipo: todos los astronautas que usan este
/// computador comparten la misma hora. <see cref="Time"/> es la hora local.
/// </summary>
public sealed record CheckUpReminderSettings(bool Enabled, TimeOnly Time)
{
    public static CheckUpReminderSettings Disabled { get; } = new(false, new TimeOnly(9, 0));
}

/// <summary>Guarda el horario del recordatorio para que sobreviva a reinicios.</summary>
public interface ICheckUpReminderSettingsStore
{
    CheckUpReminderSettings Get();

    void Save(CheckUpReminderSettings settings);
}

/// <summary>Archivo JSON junto a la base local; si falta o está dañado se usa el horario desactivado.</summary>
public sealed class FileCheckUpReminderSettingsStore : ICheckUpReminderSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public FileCheckUpReminderSettingsStore()
        : this(Path.Combine(Path.GetDirectoryName(LocalDatabase.DefaultPath)!, "checkup-reminder.json"))
    {
    }

    public FileCheckUpReminderSettingsStore(string path) => _path = path;

    public CheckUpReminderSettings Get()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return CheckUpReminderSettings.Disabled;
            }

            return JsonSerializer.Deserialize<CheckUpReminderSettings>(File.ReadAllText(_path), JsonOptions)
                ?? CheckUpReminderSettings.Disabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CheckUpReminderSettings.Disabled;
        }
    }

    public void Save(CheckUpReminderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
