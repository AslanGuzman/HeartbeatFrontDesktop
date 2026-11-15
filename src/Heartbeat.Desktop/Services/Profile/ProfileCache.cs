using System.Text.Json;

using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services.Profile;

/// <summary>
/// Perfil guardado en <c>LocalAccount.ProfileJson</c>: el último perfil que mandó el servidor y, si el astronauta lo
/// editó sin haber sincronizado todavía, esa edición pendiente (<see cref="UpdateProfileRequest"/>, con su
/// <c>UpdatedAt</c>). Se guarda así para no cambiar el esquema de la base local, que no tiene migraciones.
/// </summary>
/// <param name="Profile">Perfil tal como lo conoce el servidor.</param>
/// <param name="PendingEdit">Edición local aún sin enviar, o <c>null</c>.</param>
public sealed record CachedProfile(AstronautProfile Profile, UpdateProfileRequest? PendingEdit)
{
    /// <summary>El perfil que se muestra: el del servidor con la edición pendiente ya aplicada.</summary>
    public AstronautProfile Effective => PendingEdit is { } edit
        ? Profile with { DisplayName = edit.DisplayName, CallSign = edit.CallSign, Notes = edit.Notes, UpdatedAt = edit.UpdatedAt }
        : Profile;
}

public static class ProfileCache
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Lee el JSON guardado. Entiende también el formato anterior (un <see cref="AstronautProfile"/> a secas).</summary>
    public static CachedProfile? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("profile", out JsonElement profile))
            {
                AstronautProfile? server = profile.Deserialize<AstronautProfile>(Options);
                UpdateProfileRequest? pending = root.TryGetProperty("pendingEdit", out JsonElement edit) && edit.ValueKind == JsonValueKind.Object
                    ? edit.Deserialize<UpdateProfileRequest>(Options)
                    : null;
                return server is null || server.Id == Guid.Empty ? null : new CachedProfile(server, pending);
            }

            // Formato anterior: el perfil del servidor sin edición pendiente. Un objeto cualquiera no es un perfil.
            if (!root.TryGetProperty("userName", out _))
            {
                return null;
            }

            AstronautProfile? legacy = root.Deserialize<AstronautProfile>(Options);
            return legacy is null || legacy.Id == Guid.Empty ? null : new CachedProfile(legacy, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Write(CachedProfile cached) =>
        JsonSerializer.Serialize(new { profile = cached.Profile, pendingEdit = cached.PendingEdit }, Options);

    /// <summary>
    /// Incorpora el perfil que acaba de mandar el servidor. Gana la edición más reciente (CONTRACTS.md §5): si la
    /// edición pendiente es posterior a la del servidor se conserva; si no, el servidor tiene algo más nuevo y se descarta.
    /// </summary>
    public static CachedProfile MergeServerProfile(string? existingJson, AstronautProfile server)
    {
        UpdateProfileRequest? pending = Read(existingJson)?.PendingEdit;
        return new CachedProfile(server, pending is not null && pending.UpdatedAt > server.UpdatedAt ? pending : null);
    }
}
