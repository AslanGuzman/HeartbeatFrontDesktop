using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services;

/// <summary>Una lectura guardada, con lo que se midió y cómo se evaluó.</summary>
public sealed record ReadingRecord(ReadingInput Input, ReadingEvaluation Evaluation);

/// <summary>Un check-up: una sesión con sus lecturas y un único instante (MR-13).</summary>
public sealed class CheckUpRecord(Guid id, DateTimeOffset recordedAt, HealthStatus overallStatus, IReadOnlyList<ReadingRecord> readings)
{
    public Guid Id { get; } = id;

    public DateTimeOffset RecordedAt { get; } = recordedAt;

    public HealthStatus OverallStatus { get; } = overallStatus;

    public IReadOnlyList<ReadingRecord> Readings { get; } = readings;

    /// <summary>Cuándo llegó al servidor; <c>null</c> mientras está pendiente de sincronizar.</summary>
    public DateTimeOffset? SyncedAt { get; internal set; }

    public bool IsSynced => SyncedAt is not null;
}

/// <summary>Mensaje a control de misión (MR-11).</summary>
public sealed class MessageRecord(Guid id, string body, DateTimeOffset createdAt)
{
    public Guid Id { get; } = id;

    public string Body { get; } = body;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    /// <summary>Cuándo salió hacia control de misión; <c>null</c> mientras está en cola.</summary>
    public DateTimeOffset? SentAt { get; internal set; }

    public bool IsSent => SentAt is not null;
}

/// <summary>
/// Check-ups y mensajes del astronauta. Las pantallas solo conocen esta interfaz: la base SQLite (#8) y el motor
/// de sincronización (#32) la sustituyen a la implementación de demostración.
/// </summary>
public interface IHealthRecordStore
{
    /// <summary>Elementos (check-ups y mensajes) que aún no han salido hacia el servidor.</summary>
    int PendingCount { get; }

    /// <summary>Último momento en que algo se sincronizó, o <c>null</c> si nunca.</summary>
    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>Check-ups del usuario, del más reciente al más antiguo.</summary>
    IReadOnlyList<CheckUpRecord> GetCheckUps(string userName);

    /// <summary>Mensajes del usuario, del más reciente al más antiguo.</summary>
    IReadOnlyList<MessageRecord> GetMessages(string userName);

    CheckUpRecord AddCheckUp(string userName, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation);

    MessageRecord QueueMessage(string userName, string body);

    /// <summary>Marca como enviado todo lo pendiente. Devuelve cuántos elementos se enviaron.</summary>
    int MarkAllSynced(DateTimeOffset at);

    /// <summary>Se produce al cambiar los datos. Se entrega en el hilo de la interfaz.</summary>
    event EventHandler? Changed;
}
