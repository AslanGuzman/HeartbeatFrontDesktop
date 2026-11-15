using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

/// <summary>IAuthService de pruebas: devuelve lo ordenado y puede quedarse esperando para observar el estado "ocupado".</summary>
internal sealed class ScriptedAuthService : IAuthService
{
    public Func<string, string, Task<SignInResult>> OnSignIn { get; set; } = (_, _) => Task.FromResult(SignInResult.Fail(SignInFailure.InvalidCredentials));

    public List<(string User, string Password)> Attempts { get; } = [];

    public int SignOuts { get; private set; }

    public Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        Attempts.Add((userName, password));
        return OnSignIn(userName, password);
    }

    public void SignOut() => SignOuts++;
}

public sealed class LoginViewModelTests
{
    private readonly ScriptedAuthService _auth = new();
    private readonly FakeConnectivityService _connectivity = new();
    private readonly TestWorld _world = new(signedInUser: null);
    private readonly RecordingNavigation _navigation;

    public LoginViewModelTests()
    {
        _navigation = _world.Navigation;
    }

    private LoginViewModel Create(string mode = "Demo", SynchronizationContext? ui = null) =>
        new(_navigation, _auth, _connectivity, new CrewApiOptions(mode, "http://x/", 8), ui ?? new ImmediateSynchronizationContext());

    private static SignInResult Success(bool offline = false) =>
        SignInResult.Success(new SessionUser(Guid.NewGuid(), "ana", "Ana Rivera"), offline);

    [Fact]
    public async Task A_successful_sign_in_opens_the_dashboard()
    {
        _auth.OnSignIn = (_, _) => Task.FromResult(Success());
        LoginViewModel login = Create();
        login.Username = "ana";

        await login.SignInCommand.ExecuteAsync("demo123");

        Assert.Equal([("ana", "demo123")], _auth.Attempts);
        Assert.Equal([typeof(DashboardViewModel)], _navigation.Visited);
        Assert.Null(login.Error);
    }

    [Theory]
    [InlineData(SignInFailure.InvalidCredentials, "Usuario o contraseña incorrectos.")]
    [InlineData(SignInFailure.MissingFields, "Escribe tu usuario y tu contraseña.")]
    public async Task A_failed_sign_in_stays_on_the_login_and_shows_the_reason(SignInFailure failure, string message)
    {
        _auth.OnSignIn = (_, _) => Task.FromResult(SignInResult.Fail(failure));
        LoginViewModel login = Create();

        await login.SignInCommand.ExecuteAsync("x");

        Assert.Equal(message, login.Error);
        Assert.Empty(_navigation.Visited);
    }

    [Fact]
    public async Task The_first_sign_in_offline_explains_that_it_must_be_online()
    {
        _auth.OnSignIn = (_, _) => Task.FromResult(SignInResult.Fail(SignInFailure.FirstSignInNeedsConnection));
        LoginViewModel login = Create();

        await login.SignInCommand.ExecuteAsync("x");

        Assert.Contains("primer acceso", login.Error);
    }

    [Fact]
    public async Task The_error_clears_on_the_next_attempt()
    {
        LoginViewModel login = Create();
        await login.SignInCommand.ExecuteAsync("mala");
        Assert.NotNull(login.Error);

        _auth.OnSignIn = (_, _) => Task.FromResult(Success());
        await login.SignInCommand.ExecuteAsync("buena");

        Assert.Null(login.Error);
    }

    [Fact]
    public async Task A_missing_password_is_passed_on_as_empty()
    {
        LoginViewModel login = Create();

        await login.SignInCommand.ExecuteAsync(null);

        Assert.Equal([("", "")], _auth.Attempts);
    }

    // ---- Ocupado ----

    [Fact]
    public async Task While_signing_in_the_screen_is_busy_and_cannot_be_submitted_twice()
    {
        TaskCompletionSource<SignInResult> gate = new();
        _auth.OnSignIn = (_, _) => gate.Task;
        LoginViewModel login = Create();

        Task pending = login.SignInCommand.ExecuteAsync("clave");

        Assert.True(login.IsBusy);
        Assert.False(login.IsNotBusy);
        Assert.Equal("Entrando…", login.SignInText);
        Assert.False(login.SignInCommand.CanExecute("clave"));

        gate.SetResult(Success());
        await pending;

        Assert.False(login.IsBusy);
        Assert.Equal("Entrar", login.SignInText);
        Assert.True(login.SignInCommand.CanExecute("clave"));
    }

    [Fact]
    public async Task An_unexpected_failure_shows_a_generic_error_and_frees_the_screen()
    {
        _auth.OnSignIn = (_, _) => throw new InvalidOperationException("boom");
        LoginViewModel login = Create();

        await login.SignInCommand.ExecuteAsync("clave");

        Assert.Contains("problema inesperado", login.Error);
        Assert.False(login.IsBusy);
        Assert.Empty(_navigation.Visited);
    }

    // ---- Sin conexión ----

    [Fact]
    public void Online_there_is_no_offline_notice()
    {
        LoginViewModel login = Create();

        Assert.True(login.IsOnline);
        Assert.Null(login.OfflineNotice);
    }

    [Fact]
    public void Offline_the_notice_explains_what_can_be_done()
    {
        _connectivity.Set(false);
        LoginViewModel login = Create();

        Assert.False(login.IsOnline);
        Assert.Contains("ya hayas usado en este equipo", login.OfflineNotice);
        Assert.Contains("primer acceso debe ser en línea", login.OfflineNotice);
    }

    [Fact]
    public void The_notice_follows_the_connection_while_the_screen_is_open()
    {
        LoginViewModel login = Create();
        List<string?> changed = [];
        login.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _connectivity.Set(false);

        Assert.NotNull(login.OfflineNotice);
        Assert.Contains(nameof(LoginViewModel.OfflineNotice), changed);

        _connectivity.Set(true);

        Assert.Null(login.OfflineNotice);
    }

    [Fact]
    public void Connection_changes_are_delivered_on_the_ui_context()
    {
        QueueingSynchronizationContext ui = new();
        LoginViewModel login = Create(ui: ui);
        List<string?> changed = [];
        login.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _connectivity.Set(false);

        Assert.Empty(changed);
        ui.RunPending();
        Assert.Contains(nameof(LoginViewModel.OfflineNotice), changed);
    }

    [Fact]
    public void Disposing_stops_following_the_connection()
    {
        LoginViewModel login = Create();
        login.Dispose();

        _connectivity.Set(false);

        // IsOnline se lee en vivo, pero ya no se notifican cambios.
        List<string?> changed = [];
        login.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _connectivity.Set(true);
        Assert.Empty(changed);
    }

    // ---- Modo demostración ----

    [Fact]
    public void In_demo_mode_the_hint_lists_the_demo_accounts_and_password()
    {
        LoginViewModel login = Create("Demo");

        Assert.Contains("Modo demostración", login.DemoHint);
        Assert.Contains("ana", login.DemoHint);
        Assert.Contains(DemoAccounts.Password, login.DemoHint);
    }

    [Fact]
    public void With_the_real_server_no_credentials_are_shown()
    {
        Assert.Null(Create("Live").DemoHint);
    }

    [Fact]
    public void The_login_hides_the_menu()
    {
        Assert.False(Create().ShowsNavigation);
    }
}
