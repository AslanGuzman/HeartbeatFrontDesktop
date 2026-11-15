namespace Heartbeat.Desktop.Services;

/// <summary>
/// Estado de sincronización que muestra la barra de estado. Lo implementará el motor de sync (#32)
/// sobre las tablas locales (#8); mientras tanto se registran la sincronización simulada (modo demostración) o un proveedor que solo informa de lo pendiente (servidor real).
/// </summary>
public interface ISyncStatusProvider
{
    /// <summary>Momento (UTC) de la última sincronización correcta; <c>null</c> si nunca se ha sincronizado.</summary>
    DateTimeOffset? LastSyncAt { get; }

    /// <summary>Elementos locales (check-ups, mensajes y ediciones de perfil) que aún no se han enviado al servidor.</summary>
    int PendingCount { get; }

    /// <summary>Se produce cuando cambia <see cref="LastSyncAt"/> o <see cref="PendingCount"/>. Puede dispararse en cualquier hilo.</summary>
    event EventHandler? Changed;
}
