using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services;

/// <summary>Una lectura guardada, con lo que se midió y cómo se evaluó.</summary>
/// <param name="Source">De dónde viene: tecleada por el astronauta (<c>Manual</c>) o importada del reloj (<c>Wearable</c>).</param>
public sealed record ReadingRecord(ReadingInput Input, ReadingEvaluation Evaluation, ReadingSource Source = ReadingSource.Manual)
{
    public bool FromWatch => Source == ReadingSource.Wearable;
}

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

/// <summary>Estado de un mensaje en la bandeja (MR-11).</summary>
public enum MessageStatus
{
    /// <summary>Saliente que espera conexión.</summary>
    Queued,

    /// <summary>Saliente que ya llegó a control de misión.</summary>
    Sent,

    /// <summary>Saliente que control de misión ya leyó.</summary>
    Read,

    /// <summary>Saliente que el servidor rechazó; no se reintenta.</summary>
    Rejected,

    /// <summary>Entrante (respuesta de control de misión) sin leer.</summary>
    Unread,

    /// <summary>Entrante ya leída.</summary>
    Opened,
}

/// <summary>
/// Mensaje o reporte (MR-11). Saliente (<c>toMissionControl</c>): se escribe en el equipo y sale al sincronizar; un
/// reporte adjunta un check-up. Entrante (<c>toAstronaut</c>): una respuesta de control de misión que llegó por sync.
/// </summary>
public sealed class MessageRecord(
    Guid id,
    string body,
    DateTimeOffset createdAt,
    Guid? checkUpId = null,
    string? subject = null,
    MessageDirection direction = MessageDirection.ToMissionControl,
    string? senderName = null)
{
    public Guid Id { get; } = id;

    public MessageDirection Direction { get; } = direction;

    public bool IsInbound => Direction == MessageDirection.ToAstronaut;

    /// <summary>Check-up que adjunta un reporte; <c>null</c> en un mensaje normal.</summary>
    public Guid? CheckUpId { get; } = checkUpId;

    public string? Subject { get; } = subject;

    public bool IsReport => CheckUpId is not null;

    public string Body { get; } = body;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    /// <summary>Quién escribió, en los mensajes entrantes («Dr. J. Okafor»).</summary>
    public string? SenderName { get; } = senderName;

    /// <summary>Cuándo salió hacia control de misión; <c>null</c> mientras está en cola.</summary>
    public DateTimeOffset? SentAt { get; internal set; }

    public bool IsSent => SentAt is not null;

    /// <summary>Saliente: cuándo lo leyó control de misión. Entrante: cuándo lo leyó el astronauta en el equipo.</summary>
    public DateTimeOffset? ReadAt { get; internal set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>El servidor lo rechazó (ver <see cref="SyncError"/>); no se reintenta.</summary>
    public bool IsRejected { get; internal set; }

    public string? SyncError { get; internal set; }

    public MessageStatus Status => IsInbound
        ? (IsRead ? MessageStatus.Opened : MessageStatus.Unread)
        : IsRejected ? MessageStatus.Rejected
        : IsRead ? MessageStatus.Read
        : IsSent ? MessageStatus.Sent
        : MessageStatus.Queued;
}

/// <summary>Una alerta de un check-up crítico que control de misión reconoció.</summary>
public sealed record AlertAcknowledgementRecord(Guid CheckUpId, DateTimeOffset AcknowledgedAt, string AcknowledgedBy, string? Note);

/// <summary>Una dosis de radiacion registrada, en milisieverts (NR-07).</summary>
public sealed record RadiationDoseRecord(Guid Id, decimal DoseMsv, DateTimeOffset RecordedAt, string? Note, DateTimeOffset? SyncedAt)
{
    public bool IsSynced => SyncedAt is not null;
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

    /// <summary>Mensajes y reportes que el usuario escribió a control de misión, del más reciente al más antiguo.</summary>
    IReadOnlyList<MessageRecord> GetMessages(Guid astronautId);

    /// <summary>Respuestas de control de misión recibidas por sync, de la más reciente a la más antigua.</summary>
    IReadOnlyList<MessageRecord> GetInbox(Guid astronautId);

    /// <summary>Respuestas aún sin leer.</summary>
    int GetUnreadCount(Guid astronautId);

    /// <summary>Marca una respuesta como leída. Devuelve <c>false</c> si no existe, no es suya o ya estaba leída.</summary>
    bool MarkRead(Guid astronautId, Guid messageId);

    /// <summary>Marca todas las respuestas como leídas. Devuelve cuántas cambiaron.</summary>
    int MarkAllRead(Guid astronautId);

    /// <summary>Alertas de sus check-ups críticos que control de misión reconoció, de la más reciente a la más antigua.</summary>
    IReadOnlyList<AlertAcknowledgementRecord> GetAcknowledgedAlerts(Guid astronautId);

    /// <summary>
    /// Guarda un check-up (1 a 6 lecturas, una por indicador, con un solo instante) como pendiente de sincronizar.
    /// </summary>
    CheckUpRecord AddCheckUp(Guid astronautId, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation, string? note = null);

    /// <summary>
    /// Escribe un mensaje a control de misión. Sin <paramref name="subject"/> se usa la primera línea del texto (hasta
    /// 120 caracteres).
    /// </summary>
    MessageRecord QueueMessage(Guid astronautId, string body, string? subject = null);

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

    /// <summary>Dosis de radiacion del usuario, de la mas reciente a la mas antigua.</summary>
    IReadOnlyList<RadiationDoseRecord> GetRadiationDoses(Guid astronautId);

    /// <summary>Dosis acumulada del usuario en milisieverts (suma de todas sus dosis).</summary>
    decimal GetRadiationTotal(Guid astronautId);

    /// <summary>Registra una dosis (mayor que 0 y hasta 500 mSv) como pendiente de sincronizar.</summary>
    /// <exception cref="ArgumentException">La dosis o la nota no cumplen las reglas del contrato.</exception>
    RadiationDoseRecord AddRadiationDose(Guid astronautId, decimal doseMsv, string? note = null);

    /// <summary>Se produce al cambiar los datos. Se entrega en el hilo de la interfaz.</summary>
    event EventHandler? Changed;
}
