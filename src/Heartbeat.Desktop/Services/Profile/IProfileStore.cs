using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services.Profile;

/// <summary>Límites del contrato (CONTRACTS.md §3, tabla Astronaut).</summary>
public static class ProfileLimits
{
    public const int DisplayName = 100;
    public const int CallSign = 30;
    public const int Notes = 500;
}

/// <summary>El perfil que se muestra y si tiene una edición local que aún no llegó al servidor.</summary>
public sealed record ProfileView(AstronautProfile Profile, bool HasPendingEdit);

/// <summary>
/// Perfil del astronauta guardado en el equipo. Solo se editan <c>displayName</c>, <c>callSign</c> y <c>notes</c>; la
/// edición funciona sin conexión y queda pendiente con su <c>UpdatedAt</c> hasta que la sincronización la envíe.
/// </summary>
public interface IProfileStore
{
    /// <summary>Edición pendiente en todas las cuentas del equipo (para el contador de pendientes).</summary>
    int PendingCount { get; }

    ProfileView? Get(Guid astronautId);

    /// <summary>
    /// Guarda una edición como pendiente con <c>UpdatedAt = ahora</c>. Lanza <see cref="ArgumentException"/> si el
    /// nombre está vacío o algún campo supera su límite.
    /// </summary>
    ProfileView Save(Guid astronautId, string displayName, string? callSign, string? notes);

    /// <summary>La edición pendiente tal como la espera <c>SyncRequest.Profile</c>, o <c>null</c> si no hay.</summary>
    UpdateProfileRequest? GetPendingEdit(Guid astronautId);

    /// <summary>El servidor aceptó el perfil: pasa a ser el de referencia y se limpia la edición pendiente.</summary>
    void MarkSynced(Guid astronautId, AstronautProfile serverProfile);

    /// <summary>Marca como enviadas todas las ediciones pendientes (la sincronización simulada). Devuelve cuántas.</summary>
    int MarkAllSynced();

    /// <summary>Se produce al cambiar un perfil. Se entrega en el hilo de la interfaz.</summary>
    event EventHandler? Changed;
}
