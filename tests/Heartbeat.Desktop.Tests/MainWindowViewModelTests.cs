using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly INavigationService _navigation;
    private readonly MainWindowViewModel _window;

    public MainWindowViewModelTests()
    {
        ServiceCollection services = new();
        services.AddHeartbeatDesktop();
        services.AddSingleton<Heartbeat.Desktop.Services.IConnectivityService>(new FakeConnectivityService());
        _provider = services.BuildServiceProvider();
        _navigation = _provider.GetRequiredService<INavigationService>();
        _window = _provider.GetRequiredService<MainWindowViewModel>();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Disclaimer_is_the_required_text()
    {
        Assert.Equal("Herramienta de autoevaluación, no diagnóstico médico", _window.DisclaimerText);
    }

    [Fact]
    public void Menu_has_the_five_sections_after_login_in_order()
    {
        Assert.Equal(
            ["Dashboard", "Check-up", "Historial", "Mensajes", "Perfil"],
            _window.NavigationItems.Select(item => item.Title));
    }

    [Fact]
    public void Menu_is_hidden_on_the_login_page()
    {
        _navigation.NavigateTo<LoginViewModel>();

        Assert.False(_window.IsNavigationVisible);
        Assert.DoesNotContain(_window.NavigationItems, item => item.IsSelected);
    }

    [Fact]
    public void Continue_on_login_opens_the_dashboard_and_shows_the_menu()
    {
        LoginViewModel login = _navigation.NavigateTo<LoginViewModel>();

        login.ContinueCommand.Execute(null);

        Assert.IsType<DashboardViewModel>(_window.CurrentPage);
        Assert.True(_window.IsNavigationVisible);
    }

    [Fact]
    public void Every_menu_item_navigates_to_its_page_and_becomes_the_only_selected_one()
    {
        _navigation.NavigateTo<LoginViewModel>();

        foreach (NavigationItemViewModel item in _window.NavigationItems)
        {
            _window.NavigateCommand.Execute(item);

            Assert.IsType(item.PageType, _window.CurrentPage);
            Assert.Equal([item], _window.NavigationItems.Where(other => other.IsSelected));
        }
    }

    [Fact]
    public void Sign_out_returns_to_login()
    {
        _navigation.NavigateTo<DashboardViewModel>();

        _window.SignOutCommand.Execute(null);

        Assert.IsType<LoginViewModel>(_window.CurrentPage);
        Assert.False(_window.IsNavigationVisible);
    }

    [Fact]
    public void Raises_property_changed_when_the_page_changes()
    {
        List<string?> changed = [];
        _window.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _navigation.NavigateTo<HistoryViewModel>();

        Assert.Contains(nameof(MainWindowViewModel.CurrentPage), changed);
        Assert.Contains(nameof(MainWindowViewModel.IsNavigationVisible), changed);
    }
}
