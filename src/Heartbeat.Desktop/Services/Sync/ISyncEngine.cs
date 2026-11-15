namespace Heartbeat.Desktop.Services.Sync;

/// <summary>Qué pasó en un intento de sincronización.</summary>
public enum SyncOutcome
{
    /// <summary>No había nada que hacer (sin sesión abierta).</summary>
    Skipped,

    /// <summary>Se sincronizó.</summary>
    Completed,

    /// <summary>Sin conexión: se espera a que vuelva el enlace.</summary>
    Offline,

    /// <summary>No hay sesión con el servidor (token ausente o caducado): hay que volver a entrar con conexión.</summary>
    NeedsSignIn,

    /// <summary>Hay red pero el servidor no responde; se reintenta en el siguiente ciclo.</summary>
    ServerUnreachable,

    /// <summary>Falló por otra causa; lo pendiente se conserva y se reintenta.</summary>
    Failed,
}

/// <summary>Un elemento que el servidor rechazó, con el motivo que dio.</summary>
public sealed record SyncRejection(Guid Id, string Kind, string Reason);

/// <summary>Resumen de una sincronización correcta.</summary>
public sealed record SyncReport(
    int CheckUpsSent,
    int MessagesSent,
    IReadOnlyList<SyncRejection> Rejected,
    int InboundMessages,
    int AcknowledgedAlerts,
    bool ProfileSynced,
    bool RangesDownloaded,
    DateTimeOffset ServerTime,
    int RadiationDosesSent = 0);

public sealed record SyncResult(SyncOutcome Outcome, SyncReport? Report = null, string? Detail = null)
{
    public static SyncResult Of(SyncOutcome outcome, string? detail = null) => new(outcome, null, detail);
}

/// <summary>
/// Sube lo pendiente y baja lo nuevo en segundo plano, sin bloquear la interfaz (MR-10, CONTRACTS.md §5): al abrir la
/// sesión, tras guardar, al recuperar el enlace y cada 60 s con conexión.
/// </summary>
public interface ISyncEngine
{
    /// <summary>Pide una sincronización y vuelve enseguida; varias peticiones seguidas se agrupan en una.</summary>
    void RequestSync();

    /// <summary>Sincroniza ahora y espera el resultado. Si ya hay una en curso, espera a que termine y hace otra.</summary>
    Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Se produce al terminar cada intento (también los que no hicieron nada). Puede dispararse en cualquier hilo.</summary>
    event EventHandler<SyncResult>? Completed;
}

public sealed record SyncOptions(TimeSpan Interval, int BatchSize)
{
    /// <summary>60 s entre intentos y lotes de hasta 500 check-ups (CONTRACTS.md §5).</summary>
    public static SyncOptions Default { get; } = new(TimeSpan.FromSeconds(60), 500);
}
