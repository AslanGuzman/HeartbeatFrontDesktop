namespace Heartbeat.Desktop.Services;

/// <summary>
/// Estado de sincronización que muestra la barra de estado. Lo implementa el motor de sincronización
/// (<c>SyncEngine</c>, #32) sobre las tablas locales (#8).
/// </summary>
public interface ISyncStatusProvider
{
    /// <summary>Momento (UTC) de la última sincronización correcta; <c>null</c> si nunca se ha sincronizado.</summary>
    DateTimeOffset? LastSyncAt { get; }

    /// <summary>Elementos del astronauta con la sesión abierta (check-ups, mensajes y su edición de perfil) que aún no se han enviado.</summary>
    int PendingCount { get; }

    /// <summary>Elementos que el servidor rechazó. No se reintentan: se muestran con su motivo en el historial y en Mensajes.</summary>
    int RejectedCount { get; }

    /// <summary>Se está sincronizando ahora mismo.</summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Aviso sobre el último intento («Vuelve a iniciar sesión…», «No se pudo sincronizar…»); <c>null</c> si todo va bien.
    /// </summary>
    string? StatusText { get; }

    /// <summary>Pide una sincronización ahora (no espera a que termine).</summary>
    void SyncNow();

    /// <summary>Se produce cuando cambia cualquiera de las propiedades anteriores. Puede dispararse en cualquier hilo.</summary>
    event EventHandler? Changed;
}
