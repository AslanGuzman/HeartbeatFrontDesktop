using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services;

/// <summary>Una lectura guardada, con lo que se midió y cómo se evaluó.</summary>
public sealed record ReadingRecord(ReadingInput Input, ReadingEvaluation Evaluation);

/// <summary>Un check-up: una sesión con sus lecturas y un único instante (MR-13).</summary>
public sealed class CheckUpRecord(Guid id, DateTimeOffset recordedAt, HealthStatus overallStatus, IReadOnlyList<ReadingRecord> readings, string? note = null)
{
    public Guid Id { get; } = id;

    public DateTimeOffset RecordedAt { get; } = recordedAt;

    public HealthStatus OverallStatus { get; } = overallStatus;

    public IReadOnlyList<ReadingRecord> Readings { get; } = readings;

    /// <summary>Nota libre del astronauta (hasta 500 caracteres).</summary>
    public string? Note { get; } = note;

    /// <summary>Cuándo llegó al servidor; <c>null</c> mientras está pendiente de sincronizar.</summary>
    public DateTimeOffset? SyncedAt { get; internal set; }

    public bool IsSynced => SyncedAt is not null;

    /// <summary>El servidor lo rechazó (ver <see cref="SyncError"/>); no se reintenta.</summary>
    public bool IsRejected { get; internal set; }

    /// <summary>Motivo del rechazo que dio el servidor.</summary>
    public string? SyncError { get; internal set; }
}

/// <summary>Mensaje a control de misión (MR-11).</summary>
public sealed class MessageRecord(Guid id, string body, DateTimeOffset createdAt, Guid? checkUpId = null, string? subject = null)
{
    /// <summary>Check-up que adjunta un reporte; <c>null</c> en un mensaje normal.</summary>
    public Guid? CheckUpId { get; } = checkUpId;

    public string? Subject { get; } = subject;

    public bool IsReport => CheckUpId is not null;

    public Guid Id { get; } = id;

    public string Body { get; } = body;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    /// <summary>Cuándo salió hacia control de misión; <c>null</c> mientras está en cola.</summary>
    public DateTimeOffset? SentAt { get; internal set; }

    public bool IsSent => SentAt is not null;

    /// <summary>El servidor lo rechazó (ver <see cref="SyncError"/>); no se reintenta.</summary>
    public bool IsRejected { get; internal set; }

    public string? SyncError { get; internal set; }
}

/// <summary>
/// Check-ups y mensajes del astronauta. Las pantallas solo conocen esta interfaz; la implementación guarda en la
/// base SQLite local y el motor de sincronización (#32) se encarga de enviar lo pendiente.
/// </summary>
public interface IHealthRecordStore
{
    /// <summary>Elementos (check-ups y mensajes) que aún no han salido hacia el servidor.</summary>
    int PendingCount { get; }

    /// <summary>Check-ups y mensajes pendientes de un astronauta.</summary>
    int GetPendingCount(Guid astronautId);

    /// <summary>Check-ups y mensajes de un astronauta que el servidor rechazó.</summary>
    int GetRejectedCount(Guid astronautId);

    /// <summary>Último momento en que algo se sincronizó, o <c>null</c> si nunca.</summary>
    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>Check-ups del usuario, del más reciente al más antiguo.</summary>
    IReadOnlyList<CheckUpRecord> GetCheckUps(Guid astronautId);

    /// <summary>Mensajes del usuario, del más reciente al más antiguo.</summary>
    IReadOnlyList<MessageRecord> GetMessages(Guid astronautId);

    /// <summary>
    /// Guarda un check-up (1 a 6 lecturas, una por indicador, con un solo instante) como pendiente de sincronizar.
    /// </summary>
    CheckUpRecord AddCheckUp(Guid astronautId, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation, string? note = null);

    MessageRecord QueueMessage(Guid astronautId, string body);

    /// <summary>Encola un reporte a control de misión que adjunta el check-up indicado (<c>Kind = report</c>).</summary>
    MessageRecord QueueReport(Guid astronautId, Guid checkUpId, string subject, string body);

    /// <summary>Indica si ya se envió (o se encoló) un reporte de ese check-up.</summary>
    bool HasReport(Guid checkUpId);

    /// <summary>Marca como enviado todo lo pendiente. Devuelve cuántos elementos se enviaron.</summary>
    int MarkAllSynced(DateTimeOffset at);

    /// <summary>
    /// Avisa de que los datos cambiaron por fuera del almacén (el motor de sincronización marcó elementos como enviados o
    /// rechazados, o guardó mensajes entrantes), para que las pantallas abiertas se actualicen.
    /// </summary>
    void NotifyChanged();

    /// <summary>Se produce al cambiar los datos. Se entrega en el hilo de la interfaz.</summary>
    event EventHandler? Changed;
}
