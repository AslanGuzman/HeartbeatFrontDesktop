using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class LoginViewModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly INavigationService _navigation;
    private readonly LoginViewModel _login;

    public LoginViewModelTests()
    {
        ServiceCollection services = new();
        services.AddHeartbeatDesktop();
        _provider = services.BuildServiceProvider();
        _navigation = _provider.GetRequiredService<INavigationService>();
        _login = _navigation.NavigateTo<LoginViewModel>();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("ana")]
    [InlineData("  Luis ")]
    [InlineData("MARA")]
    public void A_demo_account_with_the_right_password_opens_the_dashboard(string user)
    {
        _login.Username = user;

        _login.SignInCommand.Execute(DemoAuthenticator.DemoPassword);

        Assert.IsType<DashboardViewModel>(_navigation.CurrentPage);
    }

    [Theory]
    [InlineData("ana", "mala")]
    [InlineData("ana", "DEMO123")]
    [InlineData("nadie", "demo123")]
    public void Wrong_credentials_keep_the_login_and_show_an_error(string user, string password)
    {
        _login.Username = user;

        _login.SignInCommand.Execute(password);

        Assert.Same(_login, _navigation.CurrentPage);
        Assert.Equal("Usuario o contraseña incorrectos.", _login.Error);
    }

    [Theory]
    [InlineData("", "demo123")]
    [InlineData("ana", "")]
    [InlineData("ana", null)]
    [InlineData("   ", "demo123")]
    public void Missing_fields_ask_for_both(string user, string? password)
    {
        _login.Username = user;

        _login.SignInCommand.Execute(password);

        Assert.Same(_login, _navigation.CurrentPage);
        Assert.Equal("Escribe tu usuario y tu contraseña.", _login.Error);
    }

    [Fact]
    public void The_login_hides_the_menu_and_the_demo_hint_lists_the_accounts()
    {
        Assert.False(_login.ShowsNavigation);
        Assert.Contains("ana", _login.DemoHint);
        Assert.Contains("demo123", _login.DemoHint);
    }
}
