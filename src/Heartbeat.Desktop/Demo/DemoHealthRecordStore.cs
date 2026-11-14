using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Almacén en memoria con datos de ejemplo para los tres astronautas de demostración. Se pierde al cerrar la app.
/// Provisional: la base SQLite local (#8) y el motor de sincronización (#32) lo sustituyen.
/// </summary>
public sealed class DemoHealthRecordStore : IHealthRecordStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CheckUpRecord>> _checkUps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MessageRecord>> _messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _uiContext;
    private DateTimeOffset? _lastSyncedAt;

    /// <param name="uiContext">
    /// Contexto al que se entrega <see cref="Changed"/>. Si no se indica (lo normal con DI) se usa el del hilo que
    /// crea el almacén, que es el de la interfaz.
    /// </param>
    public DemoHealthRecordStore(IHealthEvaluator evaluator, TimeProvider clock, SynchronizationContext? uiContext = null)
    {
        _clock = clock;
        _uiContext = uiContext ?? SynchronizationContext.Current;
        Seed(evaluator);
    }

    public event EventHandler? Changed;

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _checkUps.Values.Sum(list => list.Count(c => !c.IsSynced))
                     + _messages.Values.Sum(list => list.Count(m => !m.IsSent));
            }
        }
    }

    public DateTimeOffset? LastSyncedAt
    {
        get
        {
            lock (_gate)
            {
                return _lastSyncedAt;
            }
        }
    }

    public IReadOnlyList<CheckUpRecord> GetCheckUps(string userName)
    {
        lock (_gate)
        {
            return _checkUps.TryGetValue(userName ?? string.Empty, out List<CheckUpRecord>? list)
                ? list.OrderByDescending(c => c.RecordedAt).ToList()
                : [];
        }
    }

    public IReadOnlyList<MessageRecord> GetMessages(string userName)
    {
        lock (_gate)
        {
            return _messages.TryGetValue(userName ?? string.Empty, out List<MessageRecord>? list)
                ? list.OrderByDescending(m => m.CreatedAt).ToList()
                : [];
        }
    }

    public CheckUpRecord AddCheckUp(string userName, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(evaluation);

        CheckUpRecord record = Build(_clock.GetUtcNow(), inputs, evaluation);
        lock (_gate)
        {
            ListFor(_checkUps, userName).Add(record);
        }

        RaiseChanged();
        return record;
    }

    public MessageRecord QueueMessage(string userName, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        MessageRecord record = new(Guid.CreateVersion7(), body.Trim(), _clock.GetUtcNow());
        lock (_gate)
        {
            ListFor(_messages, userName).Add(record);
        }

        RaiseChanged();
        return record;
    }

    public int MarkAllSynced(DateTimeOffset at)
    {
        int sent = 0;
        lock (_gate)
        {
            foreach (CheckUpRecord checkUp in _checkUps.Values.SelectMany(l => l).Where(c => !c.IsSynced))
            {
                checkUp.SyncedAt = at;
                sent++;
            }

            foreach (MessageRecord message in _messages.Values.SelectMany(l => l).Where(m => !m.IsSent))
            {
                message.SentAt = at;
                sent++;
            }

            if (sent > 0)
            {
                _lastSyncedAt = at;
            }
        }

        if (sent > 0)
        {
            RaiseChanged();
        }

        return sent;
    }

    private static List<T> ListFor<T>(Dictionary<string, List<T>> map, string userName)
    {
        if (!map.TryGetValue(userName, out List<T>? list))
        {
            map[userName] = list = [];
        }

        return list;
    }

    private static CheckUpRecord Build(DateTimeOffset at, IReadOnlyList<ReadingInput> inputs, CheckUpEvaluation evaluation)
    {
        List<ReadingRecord> readings = inputs
            .Select((input, index) => new ReadingRecord(input, evaluation.Readings[index]))
            .ToList();
        return new CheckUpRecord(Guid.CreateVersion7(), at, evaluation.OverallStatus, readings);
    }

    private void RaiseChanged()
    {
        if (_uiContext is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _uiContext.Post(static state => ((DemoHealthRecordStore)state!).Changed?.Invoke(state, EventArgs.Empty), this);
        }
    }

    // Datos de ejemplo: cada astronauta tiene un historial distinto para que el panel muestre los tres estados.
    private void Seed(IHealthEvaluator evaluator)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        void Add(string user, TimeSpan ago, decimal sys, decimal dia, decimal hr, decimal sugar, decimal temp, decimal sleep, decimal mood)
        {
            List<ReadingInput> inputs =
            [
                new(IndicatorType.BloodPressure, sys, dia),
                new(IndicatorType.HeartRate, hr),
                new(IndicatorType.BloodSugar, sugar),
                new(IndicatorType.BodyTemperature, temp),
                new(IndicatorType.Sleep, sleep),
                new(IndicatorType.Mood, mood),
            ];
            CheckUpRecord record = Build(now - ago, inputs, evaluator.Evaluate(inputs));
            record.SyncedAt = now - ago + TimeSpan.FromMinutes(2);
            ListFor(_checkUps, user).Add(record);
            _lastSyncedAt = _lastSyncedAt is { } last && last > record.SyncedAt ? last : record.SyncedAt;
        }

        Add("ana", TimeSpan.FromDays(3), 118, 76, 64, 92, 36.6m, 7.5m, 4);
        Add("ana", TimeSpan.FromDays(2), 124, 82, 108, 96, 36.9m, 5.0m, 3);
        Add("ana", TimeSpan.FromDays(1), 120, 78, 70, 90, 36.7m, 7.0m, 4);

        Add("luis", TimeSpan.FromDays(2), 128, 84, 72, 105, 36.8m, 6.5m, 4);
        Add("luis", TimeSpan.FromDays(1), 135, 88, 96, 52, 36.7m, 4.5m, 2);

        Add("mara", TimeSpan.FromDays(1), 112, 72, 58, 88, 36.5m, 8.0m, 5);
    }
}
