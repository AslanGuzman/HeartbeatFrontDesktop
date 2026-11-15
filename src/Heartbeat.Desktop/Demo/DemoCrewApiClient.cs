using System.Net.Http;
using System.Net;

using Heartbeat.Client;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Crew API de mentira para el modo demostración: acepta a ana, luis y mara con la contraseña de demostración y
/// devuelve perfiles estables (con el mismo Id siempre, a diferencia de <c>FakeCrewApiClient</c>), de modo que los
/// datos de cada astronauta sobreviven entre sesiones. Responde como el servidor real: 401 si las credenciales
/// fallan, y en <c>/sync</c> acepta lo recibido (idempotente), aplica la edición de perfil si es más nueva y devuelve
/// el perfil solo cuando la aplicó. Para poder ver cómo se muestra un rechazo, rechaza los check-ups y mensajes cuyo
/// texto contiene «[rechazar]». También hace de control de misión: contesta a cada reporte y
/// reconoce las alertas de los check-ups críticos, para poder ver la bandeja.
/// </summary>
public sealed class DemoCrewApiClient(TimeProvider clock) : ICrewApiClient
{
    public const string RejectMarker = "[rechazar]";

    private readonly Dictionary<Guid, AstronautProfile> _serverProfiles = [];
    private readonly HashSet<Guid> _stored = [];
    private AstronautProfile? _profile;

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public string? DeviceId { get; private set; }

    public bool IsOnline => true;

#pragma warning disable CS0067
    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
#pragma warning restore CS0067

    public Task<CrewLoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        SessionUser? account = DemoAccounts.All.FirstOrDefault(a => string.Equals(a.UserName, request.UserName, StringComparison.OrdinalIgnoreCase));
        if (account is null || !string.Equals(request.Password, DemoAccounts.Password, StringComparison.Ordinal))
        {
            throw new HttpRequestException(Loc.T("demo.badCredentials"), null, HttpStatusCode.Unauthorized);
        }

        DateTimeOffset now = clock.GetUtcNow();
        _profile = _serverProfiles.TryGetValue(account.Id, out AstronautProfile? known)
            ? known
            : _serverProfiles[account.Id] = new AstronautProfile(
                account.Id,
                account.UserName,
                account.DisplayName,
                "Demo",
                new MissionInfo(Guid.Parse("0197c2a0-0000-7000-8000-0000000000ff"), "Artemis Demo 1", "station", now.AddDays(-100)),
                null,
                null,
                now.AddDays(-30));

        SetTokens("demo-access-token", "demo-refresh-token", request.DeviceId);
        return Task.FromResult(new CrewLoginResponse(AccessToken!, now.AddHours(1), RefreshToken!, now.AddDays(14), _profile));
    }

    public Task<TokenResponse> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(RefreshToken))
        {
            throw new InvalidOperationException("No refresh token is available.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        AccessToken = "demo-access-token-refreshed";
        return Task.FromResult(new TokenResponse(AccessToken, now.AddHours(1), RefreshToken, now.AddDays(14)));
    }

    public Task<AstronautProfile> GetMeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_profile is { } mine && _serverProfiles.TryGetValue(mine.Id, out AstronautProfile? current)
            ? current
            : throw new InvalidOperationException("Not logged in."));

    public Task<string> GetReferenceRangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferenceRanges.Json);

    public Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken cancellationToken = default)
    {
        List<Guid> accepted = [];
        List<SyncRejected> rejected = [];

        foreach (CheckUpDto checkUp in request.CheckUps)
        {
            if (checkUp.Note?.Contains(RejectMarker, StringComparison.OrdinalIgnoreCase) == true)
            {
                rejected.Add(new SyncRejected(checkUp.Id, Loc.T("demo.rejectedCheckUp", RejectMarker)));
            }
            else
            {
                _stored.Add(checkUp.Id);
                accepted.Add(checkUp.Id);
            }
        }

        foreach (MessageDto message in request.Messages)
        {
            if (message.Body.Contains(RejectMarker, StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add(new SyncRejected(message.Id, Loc.T("demo.rejectedMessage", RejectMarker)));
            }
            else
            {
                accepted.Add(message.Id);
            }
        }

        // Las dosis de radiación se aceptan tal cual.
        accepted.AddRange((request.RadiationDoses ?? []).Select(d => d.Id));

        // Como el servidor real: el perfil solo vuelve cuando se aplicó la edición (updatedAt estrictamente posterior).
        AstronautProfile? applied = null;
        if (request.Profile is { } edit && _profile is { } mine && _serverProfiles.TryGetValue(mine.Id, out AstronautProfile? current) && edit.UpdatedAt > current.UpdatedAt)
        {
            applied = _serverProfiles[mine.Id] = current with { DisplayName = edit.DisplayName, CallSign = edit.CallSign, Notes = edit.Notes, UpdatedAt = edit.UpdatedAt };
            _profile = applied;
        }

        // Control de misión de mentira: contesta a cada reporte y reconoce las alertas de los check-ups críticos. Los Id
        // salen del elemento al que responde, así que si el mismo lote se repite la respuesta es la misma (idempotente).
        DateTimeOffset now = clock.GetUtcNow();
        List<MessageDto> replies = request.Messages
            .Where(m => accepted.Contains(m.Id) && m.Kind == MessageKind.Report)
            .Select(m => new MessageDto(
                DerivedId(m.Id), MessageDirection.ToAstronaut, MessageKind.Message, m.CheckUpId,
                "Re: " + m.Subject, Loc.T("demo.replyBody"),
                now, now, null, Loc.T("demo.mcName")))
            .ToList();
        List<AcknowledgedAlert> alerts = request.CheckUps
            .Where(c => accepted.Contains(c.Id) && c.OverallStatus == HealthStatus.Critical)
            .Select(c => new AcknowledgedAlert(c.Id, now, Loc.T("demo.mcName"), Loc.T("demo.alertNote")))
            .ToList();

        return Task.FromResult(new SyncResponse(now, accepted, rejected, applied, new SyncInbound(replies, alerts), false));
    }

    private static Guid DerivedId(Guid source)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(source.ToByteArray());
        return new Guid(bytes);
    }

    public Task<Paged<CheckUpDto>> GetCheckUpsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Paged<CheckUpDto>([], page, pageSize, 0));

    public void SetTokens(string accessToken, string refreshToken, string deviceId)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        DeviceId = deviceId;
    }

    public void Dispose()
    {
    }
}
