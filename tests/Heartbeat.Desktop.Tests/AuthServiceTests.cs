using System.Net;
using System.Net.Http;
using System.Text.Json;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly AuthTestWorld _world = new();

    public void Dispose() => _world.Dispose();

    // ---- Primer login en línea ----

    [Fact]
    public async Task The_first_sign_in_online_opens_the_session_with_the_server_profile()
    {
        Guid id = _world.ServerAccepts("a.rivera", "clave-1", "Ana Rivera");

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.True(result.Succeeded);
        Assert.False(result.IsOffline);
        Assert.Equal(new SessionUser(id, "a.rivera", "Ana Rivera"), result.User);
        Assert.Equal(result.User, _world.Session.CurrentUser);
        Assert.False(_world.Session.IsOfflineSession);
    }

    [Fact]
    public async Task The_login_request_carries_the_user_the_password_and_this_computers_id()
    {
        _world.ServerAccepts("a.rivera", "clave-1");

        await _world.Auth.SignInAsync("  a.rivera ", "clave-1");

        LoginRequest request = Assert.Single(_world.Api.Logins);
        Assert.Equal("a.rivera", request.UserName);
        Assert.Equal("clave-1", request.Password);
        Assert.Equal("equipo-1", request.DeviceId);
    }

    [Fact]
    public async Task The_first_online_sign_in_stores_a_pbkdf2_hash_and_never_the_password()
    {
        Guid id = _world.ServerAccepts("a.rivera", "clave-1");

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        LocalAccount account = Assert.Single(_world.Accounts());
        Assert.Equal(id, account.AstronautId);
        Assert.Equal("a.rivera", account.UserName);
        Assert.StartsWith($"{LocalPasswordHasher.Iterations}.", account.PasswordHash);
        Assert.DoesNotContain("clave-1", account.PasswordHash + account.Salt + account.ProfileJson);
        Assert.True(LocalPasswordHasher.Verify("clave-1", account.PasswordHash, account.Salt));
        Assert.Equal(TestWorld.Now, account.LastOnlineLoginAt);
    }

    [Fact]
    public async Task The_profile_is_cached_for_offline_use()
    {
        _world.ServerAccepts("a.rivera", "clave-1", "Ana Rivera");

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        AstronautProfile? cached = JsonSerializer.Deserialize<AstronautProfile>(_world.Accounts().Single().ProfileJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Ana Rivera", cached!.DisplayName);
        Assert.Equal("Comet", cached.CallSign);
        Assert.Equal(new DateOnly(1988, 3, 12), cached.DateOfBirth);
    }

    [Fact]
    public async Task Tokens_are_stored_protected_and_not_in_plain_text()
    {
        Guid id = _world.ServerAccepts("a.rivera", "clave-1");

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        TokenCache tokens = Assert.Single(_world.Tokens());
        Assert.Equal(id, tokens.AstronautId);
        Assert.DoesNotContain("acceso-a.rivera", tokens.AccessToken);
        Assert.DoesNotContain("refresco-a.rivera", tokens.RefreshToken);
        Assert.Equal("acceso-a.rivera", _world.Protector.Unprotect(tokens.AccessToken));
        Assert.Equal("refresco-a.rivera", _world.Protector.Unprotect(tokens.RefreshToken));
        Assert.Equal(TestWorld.Now.AddHours(1), tokens.AccessTokenExpiresAt);
        Assert.Equal(TestWorld.Now.AddDays(14), tokens.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task A_second_online_sign_in_updates_the_same_rows_instead_of_adding_more()
    {
        _world.ServerAccepts("a.rivera", "clave-1", id: Guid.Parse("0197c2a0-0000-7000-8000-000000000042"));

        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.Single(_world.Accounts());
        Assert.Single(_world.Tokens());
    }

    // ---- Credenciales incorrectas en línea ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task The_server_rejecting_the_credentials_is_final_and_stores_nothing(HttpStatusCode status)
    {
        _world.Api.OnLogin = _ => throw ScriptedCrewApiClient.Status(status);

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "mala");

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Equal("Usuario o contraseña incorrectos.", result.Message);
        Assert.Null(_world.Session.CurrentUser);
        Assert.Empty(_world.Accounts());
        Assert.Empty(_world.Tokens());
    }

    [Fact]
    public async Task The_server_rejecting_a_password_wins_over_a_matching_saved_one()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();

        // La contraseña cambió en el servidor: la copia local ya no vale estando en línea.
        _world.Api.OnLogin = _ => throw ScriptedCrewApiClient.Status(HttpStatusCode.Unauthorized);
        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Null(_world.Session.CurrentUser);
    }

    [Theory]
    [InlineData("", "clave")]
    [InlineData("   ", "clave")]
    [InlineData("a.rivera", "")]
    public async Task Missing_fields_are_rejected_before_asking_anyone(string user, string password)
    {
        SignInResult result = await _world.Auth.SignInAsync(user, password);

        Assert.Equal(SignInFailure.MissingFields, result.Failure);
        Assert.Empty(_world.Api.Logins);
    }

    // ---- Login sin conexión ----

    [Fact]
    public async Task Offline_the_saved_account_opens_the_session_with_the_cached_profile()
    {
        Guid id = _world.ServerAccepts("a.rivera", "clave-1", "Ana Rivera");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);
        int logins = _world.Api.Logins.Count;

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.True(result.Succeeded);
        Assert.True(result.IsOffline);
        Assert.Equal(new SessionUser(id, "a.rivera", "Ana Rivera"), _world.Session.CurrentUser);
        Assert.True(_world.Session.IsOfflineSession);
        Assert.Equal(logins, _world.Api.Logins.Count);
    }

    [Fact]
    public async Task Offline_the_username_is_not_case_sensitive()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        SignInResult result = await _world.Auth.SignInAsync("A.RIVERA", "clave-1");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("otra")]
    [InlineData("CLAVE-1")]
    [InlineData("clave-1 ")]
    public async Task Offline_a_wrong_password_is_rejected(string password)
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", password);

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Null(_world.Session.CurrentUser);
    }

    [Fact]
    public async Task Offline_without_a_saved_account_the_first_sign_in_must_be_online()
    {
        _world.Connectivity.Set(false);

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.Equal(SignInFailure.FirstSignInNeedsConnection, result.Failure);
        Assert.Contains("primer acceso", result.Message);
        Assert.Empty(_world.Api.Logins);
    }

    [Fact]
    public async Task Offline_the_saved_tokens_are_given_back_to_the_client_so_sync_can_resume()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();

        // Tras cerrar sesión los tokens se borran; se vuelve a entrar en línea para guardarlos de nuevo.
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Session.SignOut();
        _world.Api.SetTokens(string.Empty, string.Empty, "x");
        _world.Connectivity.Set(false);

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.Equal("acceso-a.rivera", _world.Api.AccessToken);
        Assert.Equal("refresco-a.rivera", _world.Api.RefreshToken);
        Assert.Equal("equipo-1", _world.Api.DeviceId);
    }

    [Fact]
    public async Task Offline_tokens_that_cannot_be_decrypted_do_not_block_the_sign_in()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Session.SignOut();
        _world.Connectivity.Set(false);
        _world.Protector.FailToUnprotect = true;
        _world.Api.SetTokens(string.Empty, string.Empty, "x");

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, _world.Api.AccessToken);
    }

    // ---- Servidor sin responder ----

    [Fact]
    public async Task Online_but_with_the_server_not_answering_a_known_astronaut_still_gets_in_offline()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Api.OnLogin = _ => throw new HttpRequestException("connection refused");

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.True(result.Succeeded);
        Assert.True(result.IsOffline);
    }

    [Fact]
    public async Task A_timeout_counts_as_the_server_not_answering()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Api.OnLogin = _ => throw new TaskCanceledException("timeout", new TimeoutException());

        Assert.True((await _world.Auth.SignInAsync("a.rivera", "clave-1")).IsOffline);
    }

    [Fact]
    public async Task Online_with_the_server_down_and_no_saved_account_asks_to_retry()
    {
        _world.Api.OnLogin = _ => throw new HttpRequestException("connection refused");

        SignInResult result = await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.Equal(SignInFailure.ServerUnavailable, result.Failure);
        Assert.Contains("servidor", result.Message);
    }

    [Fact]
    public async Task A_server_error_is_treated_as_the_server_not_answering_not_as_wrong_credentials()
    {
        _world.Api.OnLogin = _ => throw ScriptedCrewApiClient.Status(HttpStatusCode.InternalServerError);

        Assert.Equal(SignInFailure.ServerUnavailable, (await _world.Auth.SignInAsync("a.rivera", "clave-1")).Failure);
    }

    [Fact]
    public async Task Cancelling_the_sign_in_is_not_mistaken_for_an_unreachable_server()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _world.Auth.SignInAsync("a.rivera", "clave-1", cts.Token));
    }

    // ---- Cambios de contraseña y de cuenta ----

    [Fact]
    public async Task Signing_in_online_with_a_new_password_replaces_the_saved_hash()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.ServerAccepts("a.rivera", "clave-2", id: _world.Accounts().Single().AstronautId);
        await _world.Auth.SignInAsync("a.rivera", "clave-2");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        Assert.True((await _world.Auth.SignInAsync("a.rivera", "clave-2")).Succeeded);
        _world.Auth.SignOut();
        Assert.Equal(SignInFailure.InvalidCredentials, (await _world.Auth.SignInAsync("a.rivera", "clave-1")).Failure);
    }

    [Fact]
    public async Task Every_online_sign_in_uses_a_fresh_salt()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        string firstSalt = _world.Accounts().Single().Salt;
        _world.Auth.SignOut();

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        Assert.NotEqual(firstSalt, _world.Accounts().Single().Salt);
    }

    [Fact]
    public async Task If_the_server_recreates_the_account_with_another_id_the_username_moves_to_the_new_one()
    {
        Guid oldId = _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        Guid newId = _world.ServerAccepts("a.rivera", "clave-1");

        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        LocalAccount account = Assert.Single(_world.Accounts());
        Assert.Equal(newId, account.AstronautId);
        Assert.NotEqual(oldId, account.AstronautId);
    }

    // ---- Cerrar sesión ----

    [Fact]
    public async Task Sign_out_closes_the_session_deletes_the_tokens_and_keeps_the_account()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");

        _world.Auth.SignOut();

        Assert.Null(_world.Session.CurrentUser);
        Assert.False(_world.Session.IsOfflineSession);
        Assert.Empty(_world.Tokens());
        Assert.Single(_world.Accounts());
        Assert.Equal(string.Empty, _world.Api.AccessToken);
        Assert.Equal(string.Empty, _world.Api.RefreshToken);
    }

    [Fact]
    public async Task After_signing_out_the_astronaut_can_still_come_back_offline()
    {
        _world.ServerAccepts("a.rivera", "clave-1");
        await _world.Auth.SignInAsync("a.rivera", "clave-1");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        Assert.True((await _world.Auth.SignInAsync("a.rivera", "clave-1")).Succeeded);
    }

    [Fact]
    public void Signing_out_without_a_session_does_nothing()
    {
        _world.Auth.SignOut();

        Assert.Null(_world.Session.CurrentUser);
    }

    // ---- Varios astronautas en la misma laptop ----

    [Fact]
    public async Task Several_astronauts_can_share_the_computer_each_with_their_own_account_and_tokens()
    {
        Guid ana = _world.ServerAccepts("a.rivera", "clave-ana", "Ana Rivera");
        await _world.Auth.SignInAsync("a.rivera", "clave-ana");
        _world.Auth.SignOut();
        Guid kenji = _world.ServerAccepts("k.sato", "clave-kenji", "Kenji Sato");
        await _world.Auth.SignInAsync("k.sato", "clave-kenji");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        Assert.Equal(ana, (await _world.Auth.SignInAsync("a.rivera", "clave-ana")).User!.Id);
        _world.Auth.SignOut();
        Assert.Equal(kenji, (await _world.Auth.SignInAsync("k.sato", "clave-kenji")).User!.Id);
        Assert.Equal(2, _world.Accounts().Count);
    }

    [Fact]
    public async Task One_astronauts_password_does_not_open_anothers_account()
    {
        _world.ServerAccepts("a.rivera", "clave-ana");
        await _world.Auth.SignInAsync("a.rivera", "clave-ana");
        _world.Auth.SignOut();
        _world.ServerAccepts("k.sato", "clave-kenji", "Kenji Sato");
        await _world.Auth.SignInAsync("k.sato", "clave-kenji");
        _world.Auth.SignOut();
        _world.Connectivity.Set(false);

        Assert.Equal(SignInFailure.InvalidCredentials, (await _world.Auth.SignInAsync("a.rivera", "clave-kenji")).Failure);
        Assert.Equal(SignInFailure.InvalidCredentials, (await _world.Auth.SignInAsync("k.sato", "clave-ana")).Failure);
    }

    [Fact]
    public async Task Each_astronaut_only_sees_their_own_check_ups_on_the_shared_computer()
    {
        HealthEvaluator evaluator = new();
        LocalHealthRecordStore store = new(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(_world.Services),
            evaluator, _world.Clock, new ImmediateSynchronizationContext());

        Guid ana = _world.ServerAccepts("a.rivera", "clave-ana");
        SessionUser anaUser = (await _world.Auth.SignInAsync("a.rivera", "clave-ana")).User!;
        ReadingInput[] anaReading = [new(IndicatorType.HeartRate, 62)];
        store.AddCheckUp(anaUser.Id, anaReading, evaluator.Evaluate(anaReading));
        store.QueueMessage(anaUser.Id, "mensaje de Ana");
        _world.Auth.SignOut();

        Guid kenji = _world.ServerAccepts("k.sato", "clave-kenji", "Kenji Sato");
        SessionUser kenjiUser = (await _world.Auth.SignInAsync("k.sato", "clave-kenji")).User!;
        ReadingInput[] kenjiReading = [new(IndicatorType.HeartRate, 88)];
        store.AddCheckUp(kenjiUser.Id, kenjiReading, evaluator.Evaluate(kenjiReading));

        Assert.Equal(ana, anaUser.Id);
        Assert.Equal(kenji, kenjiUser.Id);
        Assert.Equal([88m], store.GetCheckUps(kenjiUser.Id).Select(c => c.Readings[0].Input.Value));
        Assert.Empty(store.GetMessages(kenjiUser.Id));
        Assert.Equal([62m], store.GetCheckUps(anaUser.Id).Select(c => c.Readings[0].Input.Value));
        Assert.Equal("mensaje de Ana", store.GetMessages(anaUser.Id).Single().Body);
    }
}
