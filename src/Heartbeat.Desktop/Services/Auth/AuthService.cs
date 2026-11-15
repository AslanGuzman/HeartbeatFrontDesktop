using System.Net.Http;
using System.Net;

using Heartbeat.Client;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Services.Auth;

/// <summary>
/// Implementa CONTRACTS.md §4 para el escritorio:
/// <list type="number">
/// <item>El primer acceso es en línea (<c>POST /auth/login</c>): se guarda el perfil, un hash PBKDF2 propio en
/// <c>LocalAccount</c> y los tokens protegidos con DPAPI en <c>TokenCache</c>.</item>
/// <item>Sin conexión se valida la contraseña contra <c>LocalAccount</c> y se abre la sesión con el perfil guardado.</item>
/// <item>Una respuesta 401/400/403 del servidor es definitiva: no se cae al hash local. Si el servidor no responde
/// (o no hay red) se intenta con la cuenta local.</item>
/// </list>
/// Todos los datos se filtran por <see cref="SessionUser.Id"/> (el AstronautId del servidor), así que varios
/// astronautas pueden compartir el equipo sin ver datos ajenos.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly ICrewApiClient _client;
    private readonly IConnectivityService _connectivity;
    private readonly ISessionContext _session;
    private readonly IServiceScopeFactory _scopes;
    private readonly ITokenProtector _protector;
    private readonly IDeviceIdProvider _device;
    private readonly TimeProvider _clock;

    public AuthService(
        ICrewApiClient client,
        IConnectivityService connectivity,
        ISessionContext session,
        IServiceScopeFactory scopes,
        ITokenProtector protector,
        IDeviceIdProvider device,
        TimeProvider clock)
    {
        _client = client;
        _connectivity = connectivity;
        _session = session;
        _scopes = scopes;
        _protector = protector;
        _device = device;
        _clock = clock;
    }

    public async Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        string name = (userName ?? string.Empty).Trim();
        if (name.Length == 0 || string.IsNullOrEmpty(password))
        {
            return SignInResult.Fail(SignInFailure.MissingFields);
        }

        bool serverUnreachable = false;
        if (_connectivity.IsOnline)
        {
            try
            {
                CrewLoginResponse response = await _client.LoginAsync(new LoginRequest(name, password, _device.DeviceId), cancellationToken);
                return await CompleteOnlineAsync(password, response);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                // El servidor es la autoridad: no se acepta la contraseña guardada si él la rechaza.
                return SignInResult.Fail(SignInFailure.InvalidCredentials);
            }
            catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
            {
                serverUnreachable = true;
            }
        }

        return await SignInOfflineAsync(name, password, serverUnreachable);
    }

    public void SignOut()
    {
        Guid? astronautId = _session.CurrentUser?.Id;
        _session.SignOut();

        if (astronautId is { } id)
        {
            using IServiceScope scope = _scopes.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.TokenCache.RemoveRange(db.TokenCache.Where(t => t.AstronautId == id));
            db.SaveChanges();
        }

        // El cliente no tiene un "borrar tokens": se dejan vacíos para que no queden los del astronauta anterior.
        _client.SetTokens(string.Empty, string.Empty, _device.DeviceId);
    }

    private async Task<SignInResult> CompleteOnlineAsync(string password, CrewLoginResponse response)
    {
        AstronautProfile profile = response.Profile;
        CachedProfile merged;

        // El hash cuesta decenas de milisegundos: fuera del hilo de la interfaz.
        (string hash, string salt) = await Task.Run(() => LocalPasswordHasher.Hash(password));

        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            // Si el servidor recreó la cuenta con otro Id, el nombre de usuario (único) pasa a la cuenta nueva.
            string lower = profile.UserName.ToLowerInvariant();
            List<LocalAccount> stale = db.LocalAccounts.Where(a => a.UserName.ToLower() == lower && a.AstronautId != profile.Id).ToList();
            db.LocalAccounts.RemoveRange(stale);
            db.SaveChanges();

            LocalAccount? account = db.LocalAccounts.FirstOrDefault(a => a.AstronautId == profile.Id);
            if (account is null)
            {
                account = new LocalAccount { AstronautId = profile.Id };
                db.LocalAccounts.Add(account);
            }

            account.UserName = profile.UserName;
            account.PasswordHash = hash;
            account.Salt = salt;
            account.LastOnlineLoginAt = _clock.GetUtcNow();

            // Una edición local aún sin sincronizar que sea más reciente que el perfil del servidor se conserva.
            merged = ProfileCache.MergeServerProfile(account.ProfileJson, profile);
            account.ProfileJson = ProfileCache.Write(merged);

            TokenCache? tokens = db.TokenCache.FirstOrDefault(t => t.AstronautId == profile.Id);
            if (tokens is null)
            {
                tokens = new TokenCache { AstronautId = profile.Id };
                db.TokenCache.Add(tokens);
            }

            tokens.AccessToken = _protector.Protect(response.AccessToken);
            tokens.RefreshToken = _protector.Protect(response.RefreshToken);
            tokens.AccessTokenExpiresAt = response.AccessTokenExpiresAt;
            tokens.RefreshTokenExpiresAt = response.RefreshTokenExpiresAt;
            db.SaveChanges();
        }

        SessionUser user = new(profile.Id, profile.UserName, merged.Effective.DisplayName);
        _session.SignIn(user, offline: false);
        return SignInResult.Success(user, offline: false);
    }

    private async Task<SignInResult> SignInOfflineAsync(string name, string password, bool serverUnreachable)
    {
        LocalAccount? account;
        using (IServiceScope scope = _scopes.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            string lower = name.ToLowerInvariant();
            account = db.LocalAccounts.AsNoTracking().FirstOrDefault(a => a.UserName.ToLower() == lower);
        }

        if (account is null)
        {
            return SignInResult.Fail(serverUnreachable ? SignInFailure.ServerUnavailable : SignInFailure.FirstSignInNeedsConnection);
        }

        bool valid = await Task.Run(() => LocalPasswordHasher.Verify(password, account.PasswordHash, account.Salt));
        if (!valid)
        {
            return SignInResult.Fail(SignInFailure.InvalidCredentials);
        }

        string displayName = ProfileCache.Read(account.ProfileJson)?.Effective.DisplayName ?? account.UserName;
        LoadTokensIntoClient(account.AstronautId);

        SessionUser user = new(account.AstronautId, account.UserName, displayName);
        _session.SignIn(user, offline: true);
        return SignInResult.Success(user, offline: true);
    }

    // Para que la sincronización pueda retomar con el refresh token en cuanto vuelva el enlace.
    private void LoadTokensIntoClient(Guid astronautId)
    {
        using IServiceScope scope = _scopes.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        TokenCache? tokens = db.TokenCache.AsNoTracking().FirstOrDefault(t => t.AstronautId == astronautId);
        if (tokens is null)
        {
            return;
        }

        string? access = _protector.Unprotect(tokens.AccessToken);
        string? refresh = _protector.Unprotect(tokens.RefreshToken);
        if (access is not null && refresh is not null)
        {
            _client.SetTokens(access, refresh, _device.DeviceId);
        }
    }

    // Sin respuesta del servidor: red caída, tiempo agotado o conexión rechazada (no una respuesta de error).
    private static bool IsUnreachable(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && ex is HttpRequestException or TaskCanceledException or TimeoutException;
}
