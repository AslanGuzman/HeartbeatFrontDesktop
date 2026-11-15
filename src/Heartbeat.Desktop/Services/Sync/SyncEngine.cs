using System.Net;
using System.Net.Http;
using System.Threading.Channels;

using Heartbeat.Client;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Heartbeat.Desktop.Services.Sync;

/// <summary>
/// Motor de sincronización (MR-10, CONTRACTS.md §5). Corre en segundo plano y sincroniza al abrir la sesión, tras
/// guardar, al volver el enlace y cada 60 s con conexión. En cada intento, para el astronauta con la sesión abierta:
/// <list type="number">
/// <item>sube sus check-ups y dosis de radiación en lotes de hasta 500, del más viejo al más nuevo (con sus mensajes y reportes, sin
/// adelantarse al check-up que adjuntan, y su edición de perfil);</item>
/// <item>marca <c>synced</c> solo los Id que el servidor devolvió en <c>accepted</c>, y <c>rejected</c> —con el
/// motivo— los que rechazó; lo demás sigue pendiente;</item>
/// <item>guarda <c>serverTime</c> como <c>lastPulledAt</c> y baja los mensajes nuevos;</item>
/// <item>si <c>rangesUpdateAvailable</c>, descarga los rangos y los guarda para el próximo arranque.</item>
/// </list>
/// También es el <see cref="ISyncStatusProvider"/> que alimenta la barra de estado.
/// </summary>
public sealed class SyncEngine : BackgroundService, ISyncEngine, ISyncStatusProvider
{
    private readonly ICrewApiClient _client;
    private readonly IConnectivityService _connectivity;
    private readonly ISessionContext _session;
    private readonly IServiceScopeFactory _scopes;
    private readonly IProfileStore _profiles;
    private readonly IHealthRecordStore _store;
    private readonly RangesSource _ranges;
    private readonly IDeviceIdProvider _device;
    private readonly TimeProvider _clock;
    private readonly SyncOptions _options;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly object _stateLock = new();
    private bool _isSyncing;
    private string? _statusText;

    public SyncEngine(
        ICrewApiClient client,
        IConnectivityService connectivity,
        ISessionContext session,
        IServiceScopeFactory scopes,
        IProfileStore profiles,
        IHealthRecordStore store,
        RangesSource ranges,
        IDeviceIdProvider device,
        TimeProvider clock,
        SyncOptions? options = null)
    {
        _client = client;
        _connectivity = connectivity;
        _session = session;
        _scopes = scopes;
        _profiles = profiles;
        _store = store;
        _ranges = ranges;
        _device = device;
        _clock = clock;
        _options = options ?? SyncOptions.Default;

        // Tras guardar un check-up, un mensaje o el perfil; al volver el enlace; al abrir o cerrar la sesión.
        _store.Changed += (_, _) => RequestIfPending();
        _profiles.Changed += (_, _) => RequestIfPending();
        _connectivity.IsOnlineChanged += OnConnectivityChanged;
        _session.Changed += (_, _) =>
        {
            RaiseChanged();
            RequestSync();
        };
    }

    public event EventHandler? Changed;

    public event EventHandler<SyncResult>? Completed;

    // ---------------- ISyncStatusProvider ----------------

    public DateTimeOffset? LastSyncAt
    {
        get
        {
            if (_session.CurrentUser is not { } user)
            {
                return null;
            }

            using IServiceScope scope = _scopes.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            return db.SyncCursors.AsNoTracking().Where(c => c.AstronautId == user.Id).Select(c => c.LastPulledAt).FirstOrDefault();
        }
    }

    public int PendingCount => _session.CurrentUser is { } user
        ? _store.GetPendingCount(user.Id) + (_profiles.GetPendingEdit(user.Id) is null ? 0 : 1)
        : 0;

    public int RejectedCount => _session.CurrentUser is { } user ? _store.GetRejectedCount(user.Id) : 0;

    public bool IsSyncing
    {
        get
        {
            lock (_stateLock)
            {
                return _isSyncing;
            }
        }
    }

    public string? StatusText
    {
        get
        {
            lock (_stateLock)
            {
                return _statusText;
            }
        }
    }

    public void SyncNow() => RequestSync();

    // ---------------- ISyncEngine ----------------

    public void RequestSync() => _signal.Writer.TryWrite(true);

    public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SyncResult result = await RunAsync(cancellationToken);
            Completed?.Invoke(this, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------- Segundo plano ----------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Se espera a una petición o a que pasen 60 s, lo primero que ocurra.
            using CancellationTokenSource wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task signal = _signal.Reader.WaitToReadAsync(wake.Token).AsTask();
            Task tick = Task.Delay(_options.Interval, wake.Token);
            try
            {
                await Task.WhenAny(signal, tick);
            }
            finally
            {
                await wake.CancelAsync();
            }

            while (_signal.Reader.TryRead(out _))
            {
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SyncNowAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nada de lo que pase en un ciclo puede detener los siguientes.
            SetState(false, $"No se pudo sincronizar: {ex.Message} Se reintentará.");
        }
    }

    private void OnConnectivityChanged(object? sender, EventArgs e)
    {
        RaiseChanged();
        if (_connectivity.IsOnline)
        {
            RequestSync();
        }
    }

    private void RequestIfPending()
    {
        if (_session.CurrentUser is { } user && (_store.GetPendingCount(user.Id) > 0 || _profiles.GetPendingEdit(user.Id) is not null))
        {
            RequestSync();
        }
        else
        {
            RaiseChanged();
        }
    }

    // ---------------- Un intento ----------------

    private async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        if (_session.CurrentUser is not { } user)
        {
            SetState(false, null);
            return SyncResult.Of(SyncOutcome.Skipped);
        }

        if (!_connectivity.IsOnline)
        {
            // La barra de estado ya dice "Sin conexión"; no hace falta un aviso aparte.
            SetState(false, null);
            return SyncResult.Of(SyncOutcome.Offline);
        }

        if (string.IsNullOrEmpty(_client.AccessToken) && string.IsNullOrEmpty(_client.RefreshToken))
        {
            const string text = "Vuelve a iniciar sesión con conexión para sincronizar.";
            SetState(false, text);
            return SyncResult.Of(SyncOutcome.NeedsSignIn, text);
        }

        SetState(true, null);
        try
        {
            SyncReport report = await SyncUserAsync(user, ct);
            SetState(false, null);
            return new SyncResult(SyncOutcome.Completed, report);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
        {
            // El cliente ya intentó renovar el token: la sesión con el servidor caducó.
            const string text = "La sesión con el servidor caducó. Vuelve a iniciar sesión con conexión.";
            SetState(false, text);
            return SyncResult.Of(SyncOutcome.NeedsSignIn, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            const string text = "Sin respuesta del servidor. Se reintentará.";
            SetState(false, text);
            return SyncResult.Of(SyncOutcome.ServerUnreachable, text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            string text = $"No se pudo sincronizar: {ex.Message} Se reintentará.";
            SetState(false, text);
            return SyncResult.Of(SyncOutcome.Failed, text);
        }
    }

    private async Task<SyncReport> SyncUserAsync(SessionUser user, CancellationToken ct)
    {
        Guid id = user.Id;
        UpdateProfileRequest? profileEdit = _profiles.GetPendingEdit(id);

        int checkUpsSent = 0;
        int messagesSent = 0;
        int dosesSent = 0;
        int inbound = 0;
        int acknowledged = 0;
        bool profileSynced = false;
        bool rangesDownloaded = false;
        DateTimeOffset serverTime = _clock.GetUtcNow();
        List<SyncRejection> rejections = [];
        HashSet<Guid> messagesTried = [];
        bool first = true;

        while (true)
        {
            Batch batch = LoadBatch(id, messagesTried);
            if (!first && batch.IsEmpty)
            {
                break;
            }

            SyncRequest request = new(
                _device.DeviceId,
                ReadCursor(id),
                _ranges.LatestVersion,
                first ? profileEdit : null,
                batch.CheckUps,
                batch.Messages,
                batch.RadiationDoses);

            SyncResponse response = await _client.SyncAsync(request, ct);
            serverTime = response.ServerTime;
            foreach (MessageDto sent in batch.Messages)
            {
                messagesTried.Add(sent.Id);
            }

            ApplyResult applied = ApplyResponse(id, batch, response);
            checkUpsSent += applied.CheckUpsAccepted;
            messagesSent += applied.MessagesAccepted;
            dosesSent += applied.DosesAccepted;
            inbound += applied.InboundStored;
            acknowledged += response.Inbound?.AcknowledgedAlerts?.Count ?? 0;
            rejections.AddRange(applied.Rejections);

            if (first && profileEdit is not null)
            {
                profileSynced = await SyncProfileAsync(id, profileEdit, response, ct);
            }
            else if (response.Profile is not null && _profiles.Get(id) is not null)
            {
                // El servidor tiene un perfil más nuevo que el nuestro.
                _profiles.MarkSynced(id, response.Profile);
            }

            if (response.RangesUpdateAvailable && !rangesDownloaded)
            {
                rangesDownloaded = await DownloadRangesAsync(ct);
            }

            first = false;

            // Si el servidor no confirmó ni rechazó nada de lo enviado, repetir el lote no serviría de nada.
            if (!batch.IsEmpty && applied.Handled == 0)
            {
                throw new InvalidOperationException("El servidor no confirmó los elementos enviados.");
            }
        }

        return new SyncReport(checkUpsSent, messagesSent, rejections, inbound, acknowledged, profileSynced, rangesDownloaded, serverTime, dosesSent);
    }

    // ---------------- Lotes ----------------

    private sealed record Batch(IReadOnlyList<CheckUpDto> CheckUps, IReadOnlyList<MessageDto> Messages, IReadOnlyList<RadiationDoseDto> RadiationDoses)
    {
        public bool IsEmpty => CheckUps.Count == 0 && Messages.Count == 0 && RadiationDoses.Count == 0;
    }

    private Batch LoadBatch(Guid astronautId, HashSet<Guid> messagesAlreadyTried)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // Del más viejo al más nuevo (SQLite no ordena DateTimeOffset: se ordena en memoria).
        List<LocalCheckUp> pending = db.CheckUps.AsNoTracking().Include(c => c.Readings)
            .Where(c => c.AstronautId == astronautId && c.SyncState == SyncState.Pending)
            .ToList()
            .OrderBy(c => c.RecordedAt)
            .ToList();
        List<LocalCheckUp> thisBatch = pending.Take(_options.BatchSize).ToList();

        // Un reporte no viaja antes que el check-up que adjunta (el servidor lo comprueba en la misma llamada).
        HashSet<Guid> deferredCheckUps = pending.Skip(_options.BatchSize).Select(c => c.Id).ToHashSet();
        List<LocalMessage> messages = db.Messages.AsNoTracking()
            .Where(m => m.AstronautId == astronautId && m.Direction == MessageDirection.ToMissionControl && m.SyncState == SyncState.Pending)
            .ToList()
            .Where(m => !messagesAlreadyTried.Contains(m.Id) && (m.CheckUpId is null || !deferredCheckUps.Contains(m.CheckUpId.Value)))
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToList();

        // Las dosis de radiación viajan igual que los check-ups: del más viejo al más nuevo, de a lotes.
        List<LocalRadiationDose> doses = db.RadiationDoses.AsNoTracking()
            .Where(d => d.AstronautId == astronautId && d.SyncState == SyncState.Pending)
            .ToList()
            .OrderBy(d => d.RecordedAt)
            .Take(_options.BatchSize)
            .ToList();

        return new Batch(
            thisBatch.Select(ToDto).ToList(),
            messages.Select(ToDto).ToList(),
            doses.Select(d => new RadiationDoseDto(d.Id, d.RecordedAt, null, d.DoseMsv, d.Note)).ToList());
    }

    private static CheckUpDto ToDto(LocalCheckUp c) => new(
        c.Id,
        c.RecordedAt,
        null,
        c.OverallStatus,
        c.RangesVersion,
        c.Note,
        c.SupersedesId,
        c.Readings.OrderBy(r => r.Indicator).Select(r => new ReadingDto(
            r.Id,
            r.Indicator,
            r.Value,
            r.SecondaryValue,
            r.Status,
            string.IsNullOrEmpty(r.BandIds) ? [] : r.BandIds.Split(','),
            r.Source,
            r.DeviceId)).ToList());

    private static MessageDto ToDto(LocalMessage m) => new(
        m.Id, m.Direction, m.Kind, m.CheckUpId, m.Subject, m.Body, m.CreatedAt, null, null, null);

    // ---------------- Respuesta ----------------

    private sealed record ApplyResult(int CheckUpsAccepted, int MessagesAccepted, int DosesAccepted, int InboundStored, IReadOnlyList<SyncRejection> Rejections)
    {
        public int Handled => CheckUpsAccepted + MessagesAccepted + DosesAccepted + Rejections.Count;
    }

    private ApplyResult ApplyResponse(Guid astronautId, Batch batch, SyncResponse response)
    {
        HashSet<Guid> accepted = (response.Accepted ?? []).ToHashSet();
        Dictionary<Guid, string> rejected = (response.Rejected ?? [])
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First().Reason);

        int checkUpsAccepted = 0;
        int messagesAccepted = 0;
        int dosesAccepted = 0;
        List<SyncRejection> rejections = [];

        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // Solo se toca lo que se envió: un Id desconocido en accepted/rejected se ignora.
        List<Guid> checkUpIds = batch.CheckUps.Select(c => c.Id).ToList();
        foreach (LocalCheckUp checkUp in db.CheckUps.Where(c => checkUpIds.Contains(c.Id) && c.AstronautId == astronautId))
        {
            if (accepted.Contains(checkUp.Id))
            {
                checkUp.SyncState = SyncState.Synced;
                checkUp.ReceivedAt = response.ServerTime;
                checkUp.SyncError = null;
                checkUpsAccepted++;
            }
            else if (rejected.TryGetValue(checkUp.Id, out string? reason))
            {
                checkUp.SyncState = SyncState.Rejected;
                checkUp.SyncError = reason;
                rejections.Add(new SyncRejection(checkUp.Id, "check-up", reason));
            }
        }

        List<Guid> messageIds = batch.Messages.Select(m => m.Id).ToList();
        foreach (LocalMessage message in db.Messages.Where(m => messageIds.Contains(m.Id) && m.AstronautId == astronautId))
        {
            if (accepted.Contains(message.Id))
            {
                message.SyncState = SyncState.Synced;
                message.ReceivedAt = response.ServerTime;
                message.SyncError = null;
                messagesAccepted++;
            }
            else if (rejected.TryGetValue(message.Id, out string? reason))
            {
                message.SyncState = SyncState.Rejected;
                message.SyncError = reason;
                rejections.Add(new SyncRejection(message.Id, "mensaje", reason));
            }
        }

        List<Guid> doseIds = batch.RadiationDoses.Select(d => d.Id).ToList();
        foreach (LocalRadiationDose dose in db.RadiationDoses.Where(d => doseIds.Contains(d.Id) && d.AstronautId == astronautId))
        {
            if (accepted.Contains(dose.Id))
            {
                dose.SyncState = SyncState.Synced;
                dose.ReceivedAt = response.ServerTime;
                dose.SyncError = null;
                dosesAccepted++;
            }
            else if (rejected.TryGetValue(dose.Id, out string? reason))
            {
                dose.SyncState = SyncState.Rejected;
                dose.SyncError = reason;
                rejections.Add(new SyncRejection(dose.Id, "dosis de radiación", reason));
            }
        }

        // serverTime es el lastPulledAt de la próxima sincronización.
        SyncCursor? cursor = db.SyncCursors.FirstOrDefault(c => c.AstronautId == astronautId);
        if (cursor is null)
        {
            cursor = new SyncCursor { AstronautId = astronautId };
            db.SyncCursors.Add(cursor);
        }

        cursor.LastPulledAt = response.ServerTime;

        // Mensajes de control de misión: idempotente por Id (el servidor puede repetirlos).
        int stored = 0;
        List<MessageDto> inbound = (response.Inbound?.Messages ?? []).Where(m => m.Direction == MessageDirection.ToAstronaut).ToList();
        List<Guid> inboundIds = inbound.Select(m => m.Id).ToList();
        HashSet<Guid> known = db.Messages.Where(m => inboundIds.Contains(m.Id)).Select(m => m.Id).ToHashSet();
        foreach (MessageDto dto in inbound.Where(m => known.Add(m.Id)))
        {
            db.Messages.Add(new LocalMessage
            {
                Id = dto.Id,
                AstronautId = astronautId,
                Direction = MessageDirection.ToAstronaut,
                Kind = dto.Kind,
                CheckUpId = dto.CheckUpId,
                Subject = dto.Subject,
                Body = dto.Body,
                CreatedAt = dto.CreatedAt,
                ReceivedAt = dto.ReceivedAt ?? response.ServerTime,
                ReadAt = dto.ReadAt,
                SenderName = dto.SenderName,
                SyncState = SyncState.Synced,
            });
            stored++;
        }

        // Alertas de check-ups críticos que control de misión reconoció: la clave es el check-up, así que repetirlas
        // solo actualiza. Se ignoran las de check-ups que no son de este astronauta.
        int alertsStored = 0;
        List<AcknowledgedAlert> alerts = (response.Inbound?.AcknowledgedAlerts ?? [])
            .GroupBy(a => a.CheckUpId).Select(g => g.Last()).ToList();
        List<Guid> alertCheckUpIds = alerts.Select(a => a.CheckUpId).ToList();
        HashSet<Guid> mine = db.CheckUps.Where(c => alertCheckUpIds.Contains(c.Id) && c.AstronautId == astronautId)
            .Select(c => c.Id).ToHashSet();
        Dictionary<Guid, LocalAlertAcknowledgement> existing = db.AlertAcknowledgements
            .Where(a => alertCheckUpIds.Contains(a.CheckUpId)).ToDictionary(a => a.CheckUpId);
        foreach (AcknowledgedAlert alert in alerts.Where(a => mine.Contains(a.CheckUpId)))
        {
            if (existing.TryGetValue(alert.CheckUpId, out LocalAlertAcknowledgement? row))
            {
                row.AcknowledgedAt = alert.AcknowledgedAt;
                row.AcknowledgedBy = alert.By;
                row.Note = alert.Note;
            }
            else
            {
                db.AlertAcknowledgements.Add(new LocalAlertAcknowledgement
                {
                    CheckUpId = alert.CheckUpId,
                    AstronautId = astronautId,
                    AcknowledgedAt = alert.AcknowledgedAt,
                    AcknowledgedBy = alert.By,
                    Note = alert.Note,
                });
                alertsStored++;
            }
        }

        db.SaveChanges();

        // El motor escribe por su cuenta: las pantallas abiertas (Historial, Mensajes) deben enterarse.
        if (checkUpsAccepted + messagesAccepted + dosesAccepted + stored + alertsStored + rejections.Count > 0)
        {
            _store.NotifyChanged();
        }

        return new ApplyResult(checkUpsAccepted, messagesAccepted, dosesAccepted, stored, rejections);
    }

    private DateTimeOffset? ReadCursor(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return db.SyncCursors.AsNoTracking().Where(c => c.AstronautId == astronautId).Select(c => c.LastPulledAt).FirstOrDefault();
    }

    // ---------------- Perfil ----------------

    // El servidor solo devuelve el perfil cuando aplicó nuestra edición; si no la aplicó (tiene una igual o más nueva)
    // devuelve null y hay que bajar su copia para que la edición local no se quede pendiente para siempre.
    private async Task<bool> SyncProfileAsync(Guid id, UpdateProfileRequest edit, SyncResponse response, CancellationToken ct)
    {
        AstronautProfile? server = response.Profile;
        if (server is null)
        {
            try
            {
                server = await _client.GetMeAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
            {
                // Se queda pendiente y se reintenta en el siguiente ciclo.
                return false;
            }
        }

        _profiles.MarkSynced(id, server);

        // Sincronizada si ya no queda pendiente (o si mientras tanto hubo una edición posterior, que sí queda).
        UpdateProfileRequest? stillPending = _profiles.GetPendingEdit(id);
        return stillPending is null || stillPending.UpdatedAt > edit.UpdatedAt;
    }

    // ---------------- Rangos ----------------

    private async Task<bool> DownloadRangesAsync(CancellationToken ct)
    {
        string json = await _client.GetReferenceRangesAsync(ct);
        if (!RangesSource.TryValidate(json, out string? version) || version is null
            || string.Equals(version, _ranges.LatestVersion, StringComparison.Ordinal))
        {
            // Rangos inválidos, o la misma versión que ya tenemos: no se guarda nada ni se repite la descarga.
            return false;
        }

        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        RangesCache? cache = db.RangesCache.FirstOrDefault();
        if (cache is null)
        {
            cache = new RangesCache { Id = 1 };
            db.RangesCache.Add(cache);
        }

        cache.Version = version;
        cache.Json = json;
        db.SaveChanges();

        _ranges.MarkDownloaded(version);
        return true;
    }

    // ---------------- Estado ----------------

    private void SetState(bool syncing, string? statusText)
    {
        lock (_stateLock)
        {
            _isSyncing = syncing;
            _statusText = statusText;
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}
