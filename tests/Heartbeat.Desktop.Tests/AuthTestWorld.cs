using System.Net;
using System.Net.Http;
using System.Text;

using Heartbeat.Client;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Crew API que responde lo que cada prueba le ordena y recuerda lo que se le pidió.</summary>
internal sealed class ScriptedCrewApiClient : ICrewApiClient
{
    public List<LoginRequest> Logins { get; } = [];

    /// <summary>Qué hace el login: devolver una respuesta o lanzar la excepción que se quiera simular.</summary>
    public Func<LoginRequest, CrewLoginResponse> OnLogin { get; set; } = _ => throw new HttpRequestException("sin guion");

    public List<(string Access, string Refresh, string Device)> TokenSets { get; } = [];

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public string? DeviceId { get; private set; }

    public bool IsOnline => true;

#pragma warning disable CS0067
    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
#pragma warning restore CS0067

    public Task<CrewLoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logins.Add(request);
        CrewLoginResponse response = OnLogin(request);
        SetTokens(response.AccessToken, response.RefreshToken, request.DeviceId);
        return Task.FromResult(response);
    }

    public void SetTokens(string accessToken, string refreshToken, string deviceId)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        DeviceId = deviceId;
        TokenSets.Add((accessToken, refreshToken, deviceId));
    }

    public Task<TokenResponse> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    // ---- /sync, /me y /reference-ranges: lo que cada prueba ordene (por defecto, el servidor acepta todo) ----

    public List<SyncRequest> Syncs { get; } = [];

    public int GetMeCalls { get; private set; }

    public int GetRangesCalls { get; private set; }

    /// <summary>Decide la respuesta de <c>/sync</c> (puede lanzar para simular un fallo). Por defecto acepta todo.</summary>
    public Func<SyncRequest, Task<SyncResponse>>? OnSyncAsync { get; set; }

    public Func<AstronautProfile>? OnGetMe { get; set; }

    public Func<string>? OnGetRanges { get; set; }

    public Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Syncs.Add(request);
        return OnSyncAsync is { } custom ? custom(request) : Task.FromResult(AcceptAll(request));
    }

    public static SyncResponse AcceptAll(SyncRequest request, DateTimeOffset? serverTime = null, AstronautProfile? profile = null, bool rangesUpdate = false, SyncInbound? inbound = null) =>
        new(serverTime ?? TestWorld.Now,
            request.CheckUps.Select(c => c.Id).Concat(request.Messages.Select(m => m.Id)).ToList(),
            [],
            profile,
            inbound ?? new SyncInbound([], []),
            rangesUpdate);

    public Task<AstronautProfile> GetMeAsync(CancellationToken cancellationToken = default)
    {
        GetMeCalls++;
        return Task.FromResult(OnGetMe?.Invoke() ?? throw new HttpRequestException("sin guion"));
    }

    public Task<string> GetReferenceRangesAsync(CancellationToken cancellationToken = default)
    {
        GetRangesCalls++;
        return Task.FromResult(OnGetRanges?.Invoke() ?? throw new HttpRequestException("sin guion"));
    }

    public Task<Paged<CheckUpDto>> GetCheckUpsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Dispose()
    {
    }

    public static HttpRequestException Status(HttpStatusCode code) => new("respuesta " + (int)code, null, code);
}

/// <summary>Protector de pruebas: invierte el texto. Determinista y distinto del original, sin depender de DPAPI.</summary>
internal sealed class ReversingProtector : ITokenProtector
{
    public bool FailToUnprotect { get; set; }

    public string Protect(string plainText) => "P:" + new string(plainText.Reverse().ToArray());

    public string? Unprotect(string protectedText) =>
        FailToUnprotect || !protectedText.StartsWith("P:", StringComparison.Ordinal) ? null : new string(protectedText[2..].Reverse().ToArray());
}

/// <summary>El servicio de acceso montado sobre una base SQLite en memoria, sin servidor ni DPAPI.</summary>
internal sealed class AuthTestWorld : IDisposable
{
    private readonly TestDatabase _database = new();

    public AuthTestWorld(bool online = true)
    {
        Clock = new FixedTimeProvider(TestWorld.Now);
        Api = new ScriptedCrewApiClient();
        Connectivity = new FakeConnectivityService(online);
        Session = new SessionContext();
        Protector = new ReversingProtector();

        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IHealthEvaluator>(new HealthEvaluator());
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite(_database.ConnectionString));
        Services = services.BuildServiceProvider();
        Auth = new AuthService(Api, Connectivity, Session, Services.GetRequiredService<IServiceScopeFactory>(), Protector, new FixedDeviceId("equipo-1"), Clock);
    }

    public FixedTimeProvider Clock { get; }

    public ScriptedCrewApiClient Api { get; }

    public FakeConnectivityService Connectivity { get; }

    public SessionContext Session { get; }

    public ReversingProtector Protector { get; }

    public ServiceProvider Services { get; }

    public AuthService Auth { get; }

    /// <summary>El servidor acepta a este astronauta con esa contraseña.</summary>
    public Guid ServerAccepts(string userName, string password, string displayName = "Ana Rivera", Guid? id = null)
    {
        Guid astronautId = id ?? Guid.CreateVersion7();
        Api.OnLogin = request =>
        {
            if (!string.Equals(request.UserName, userName, StringComparison.OrdinalIgnoreCase) || request.Password != password)
            {
                throw ScriptedCrewApiClient.Status(HttpStatusCode.Unauthorized);
            }

            AstronautProfile profile = new(
                astronautId, userName, displayName, "Comet",
                new MissionInfo(Guid.NewGuid(), "Artemis Demo 1", "station", TestWorld.Now.AddDays(-30)),
                new DateOnly(1988, 3, 12), null, TestWorld.Now);
            return new CrewLoginResponse("acceso-" + userName, TestWorld.Now.AddHours(1), "refresco-" + userName, TestWorld.Now.AddDays(14), profile);
        };
        return astronautId;
    }

    public List<LocalAccount> Accounts() => WithDb(db => db.LocalAccounts.AsNoTracking().ToList());

    public List<TokenCache> Tokens() => WithDb(db => db.TokenCache.AsNoTracking().ToList());

    public T WithDb<T>(Func<LocalDbContext, T> action)
    {
        using IServiceScope scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
    }

    public void Dispose()
    {
        Services.Dispose();
        _database.Dispose();
    }

    public static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
