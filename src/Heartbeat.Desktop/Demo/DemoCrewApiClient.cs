using System.Net.Http;
using System.Net;

using Heartbeat.Client;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Crew API de mentira para el modo demostración: acepta a ana, luis y mara con la contraseña de demostración y
/// devuelve perfiles estables (con el mismo Id siempre, a diferencia de <c>FakeCrewApiClient</c>), de modo que los
/// datos de cada astronauta sobreviven entre sesiones. Responde como el servidor real: 401 si las credenciales fallan.
/// </summary>
public sealed class DemoCrewApiClient(TimeProvider clock) : ICrewApiClient
{
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
            throw new HttpRequestException("Credenciales de demostración inválidas.", null, HttpStatusCode.Unauthorized);
        }

        DateTimeOffset now = clock.GetUtcNow();
        _profile = new AstronautProfile(
            account.Id,
            account.UserName,
            account.DisplayName,
            "Demo",
            new MissionInfo(Guid.Parse("0197c2a0-0000-7000-8000-0000000000ff"), "Artemis Demo 1", "station", now.AddDays(-100)),
            null,
            null,
            now);

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
        Task.FromResult(_profile ?? throw new InvalidOperationException("Not logged in."));

    public Task<string> GetReferenceRangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferenceRanges.Json);

    public Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken cancellationToken = default)
    {
        List<Guid> accepted = request.CheckUps.Select(c => c.Id).Concat(request.Messages.Select(m => m.Id)).ToList();
        return Task.FromResult(new SyncResponse(
            clock.GetUtcNow(),
            accepted,
            [],
            _profile,
            new SyncInbound([], []),
            false));
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
