using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Data;

/// <summary>
/// Guarda check-ups y mensajes en la base SQLite local (#8). Todo lo nuevo entra con <c>SyncState = pending</c> y se
/// evalúa al instante en el equipo, sin necesitar conexión. Los estados de cada lectura se vuelven a calcular al leer
/// con las reglas de <see cref="IHealthEvaluator"/>, de modo que la guía mostrada siempre sale de los rangos vigentes.
/// </summary>
public sealed class LocalHealthRecordStore : IHealthRecordStore
{
    public const int MaxNoteLength = 500;
    public const int MaxMessageLength = 4000;
    private const int MaxSubjectLength = 120;

    private readonly IServiceScopeFactory _scopes;
    private readonly IHealthEvaluator _evaluator;
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _uiContext;

    /// <param name="uiContext">
    /// Contexto al que se entrega <see cref="Changed"/>. Si no se indica (lo normal con DI) se usa el del hilo que
    /// crea el almacén, que es el de la interfaz.
    /// </param>
    public LocalHealthRecordStore(IServiceScopeFactory scopes, IHealthEvaluator evaluator, TimeProvider clock, SynchronizationContext? uiContext = null)
    {
        _scopes = scopes;
        _evaluator = evaluator;
        _clock = clock;
        _uiContext = uiContext ?? SynchronizationContext.Current;
    }

    public event EventHandler? Changed;

    public int PendingCount
    {
        get
        {
            using IServiceScope scope = _scopes.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            return db.CheckUps.Count(c => c.SyncState == SyncState.Pending)
                 + db.Messages.Count(m => m.Direction == MessageDirection.ToMissionControl && m.SyncState == SyncState.Pending);
        }
    }

    public DateTimeOffset? LastSyncedAt
    {
        get
        {
            using IServiceScope scope = _scopes.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            // SQLite no sabe ordenar ni agregar DateTimeOffset: se compara en memoria.
            List<DateTimeOffset?> received = db.CheckUps.Where(c => c.ReceivedAt != null).Select(c => c.ReceivedAt)
                .Concat(db.Messages.Where(m => m.Direction == MessageDirection.ToMissionControl && m.ReceivedAt != null).Select(m => m.ReceivedAt))
                .ToList();
            return received.Count == 0 ? null : received.Max();
        }
    }

    public IReadOnlyList<CheckUpRecord> GetCheckUps(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        return db.CheckUps.AsNoTracking().Include(c => c.Readings)
            .Where(c => c.AstronautId == astronautId)
            .ToList()
            .OrderByDescending(c => c.RecordedAt)
            .Select(ToRecord)
            .ToList();
    }

    public IReadOnlyList<MessageRecord> GetMessages(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        return db.Messages.AsNoTracking()
            .Where(m => m.AstronautId == astronautId && m.Direction == MessageDirection.ToMissionControl)
            .ToList()
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new MessageRecord(m.Id, m.Body, m.CreatedAt, m.Kind == MessageKind.Report ? m.CheckUpId : null, m.Subject)
            {
                SentAt = m.SyncState == SyncState.Synced ? m.ReceivedAt : null,
            })
            .ToList();
    }

    public CheckUpRecord AddCheckUp(Guid astronautId, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(evaluation);

        LocalCheckUp entity = ToEntity(astronautId, _clock.GetUtcNow(), inputs, evaluation, note);

        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.CheckUps.Add(entity);
            db.SaveChanges();
        }

        RaiseChanged();
        return ToRecord(entity);
    }

    public MessageRecord QueueMessage(Guid astronautId, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        string text = body.Trim();
        LocalMessage entity = NewOutgoing(astronautId, MessageKind.Message, null, SubjectOf(text), text);
        Save(entity);
        return new MessageRecord(entity.Id, entity.Body, entity.CreatedAt);
    }

    public MessageRecord QueueReport(Guid astronautId, Guid checkUpId, string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            if (!db.CheckUps.Any(c => c.Id == checkUpId && c.AstronautId == astronautId))
            {
                throw new ArgumentException("El check-up no existe o no es de este astronauta.", nameof(checkUpId));
            }
        }

        string cleanSubject = subject.Trim().Length <= MaxSubjectLength ? subject.Trim() : subject.Trim()[..(MaxSubjectLength - 1)] + "…";
        LocalMessage entity = NewOutgoing(astronautId, MessageKind.Report, checkUpId, cleanSubject, body.Trim());
        Save(entity);
        return new MessageRecord(entity.Id, entity.Body, entity.CreatedAt, checkUpId, entity.Subject);
    }

    public bool HasReport(Guid checkUpId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return db.Messages.Any(m => m.Kind == MessageKind.Report && m.CheckUpId == checkUpId);
    }

    private LocalMessage NewOutgoing(Guid astronautId, MessageKind kind, Guid? checkUpId, string subject, string body)
    {
        if (body.Length > MaxMessageLength)
        {
            throw new ArgumentException($"El mensaje no puede superar {MaxMessageLength} caracteres.", nameof(body));
        }

        return new LocalMessage
        {
            Id = Guid.CreateVersion7(),
            AstronautId = astronautId,
            Direction = MessageDirection.ToMissionControl,
            Kind = kind,
            CheckUpId = checkUpId,
            Subject = subject,
            Body = body,
            CreatedAt = _clock.GetUtcNow(),
            SyncState = SyncState.Pending,
        };
    }

    private void Save(LocalMessage entity)
    {
        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.Messages.Add(entity);
            db.SaveChanges();
        }

        RaiseChanged();
    }

    public int MarkAllSynced(DateTimeOffset at)
    {
        int sent;
        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            List<LocalCheckUp> checkUps = db.CheckUps.Where(c => c.SyncState == SyncState.Pending).ToList();
            List<LocalMessage> messages = db.Messages
                .Where(m => m.Direction == MessageDirection.ToMissionControl && m.SyncState == SyncState.Pending)
                .ToList();

            foreach (LocalCheckUp checkUp in checkUps)
            {
                checkUp.SyncState = SyncState.Synced;
                checkUp.ReceivedAt = at;
            }

            foreach (LocalMessage message in messages)
            {
                message.SyncState = SyncState.Synced;
                message.ReceivedAt = at;
            }

            sent = checkUps.Count + messages.Count;
            if (sent > 0)
            {
                db.SaveChanges();
            }
        }

        if (sent > 0)
        {
            RaiseChanged();
        }

        return sent;
    }

    /// <summary>Convierte un check-up evaluado en las filas locales, aplicando las reglas del contrato (1–6 lecturas, una por indicador).</summary>
    internal static LocalCheckUp ToEntity(Guid astronautId, DateTimeOffset recordedAt, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation, string? note)
    {
        if (inputs.Count is < 1 or > 6)
        {
            throw new ArgumentException("Un check-up lleva entre 1 y 6 lecturas.", nameof(inputs));
        }

        if (inputs.Select(i => i.Indicator).Distinct().Count() != inputs.Count)
        {
            throw new ArgumentException("Un check-up lleva como máximo una lectura por indicador.", nameof(inputs));
        }

        if (evaluation.Readings.Count != inputs.Count)
        {
            throw new ArgumentException("La evaluación no corresponde a las lecturas indicadas.", nameof(evaluation));
        }

        string? cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanNote?.Length > MaxNoteLength)
        {
            throw new ArgumentException($"La nota no puede superar {MaxNoteLength} caracteres.", nameof(note));
        }

        Guid id = Guid.CreateVersion7();
        return new LocalCheckUp
        {
            Id = id,
            AstronautId = astronautId,
            RecordedAt = recordedAt,
            OverallStatus = evaluation.OverallStatus,
            RangesVersion = evaluation.RangesVersion,
            Note = cleanNote,
            SyncState = SyncState.Pending,
            Readings = inputs.Select((input, index) => new LocalReading
            {
                Id = Guid.CreateVersion7(),
                CheckUpId = id,
                Indicator = input.Indicator,
                Value = input.Value,
                SecondaryValue = input.SecondaryValue,
                Status = evaluation.Readings[index].Status,
                BandIds = string.Join(",", evaluation.Readings[index].BandIds),
            }).ToList(),
        };
    }

    private CheckUpRecord ToRecord(LocalCheckUp entity)
    {
        List<ReadingRecord> readings = entity.Readings
            .OrderBy(r => r.Indicator)
            .Select(r =>
            {
                ReadingInput input = new(r.Indicator, r.Value, r.SecondaryValue);
                return new ReadingRecord(input, _evaluator.Evaluate(input));
            })
            .ToList();

        return new CheckUpRecord(entity.Id, entity.RecordedAt, entity.OverallStatus, readings, entity.Note)
        {
            SyncedAt = entity.SyncState == SyncState.Synced ? entity.ReceivedAt : null,
        };
    }

    private static string SubjectOf(string body)
    {
        string firstLine = body.Split('\n', 2)[0].Trim();
        return firstLine.Length <= MaxSubjectLength ? firstLine : firstLine[..(MaxSubjectLength - 1)] + "…";
    }

    private void RaiseChanged()
    {
        if (_uiContext is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _uiContext.Post(static state => ((LocalHealthRecordStore)state!).Changed?.Invoke(state, EventArgs.Empty), this);
        }
    }
}
