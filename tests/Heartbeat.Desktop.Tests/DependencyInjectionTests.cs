using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void The_container_graph_is_valid()
    {
        ServiceCollection services = new();
        services.AddHeartbeatDesktop();

        // ValidateOnBuild comprueba que todo lo registrado (incluida MainWindow) tiene sus dependencias resueltas,
        // sin necesidad de crear la ventana (que exige un hilo STA).
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<MainWindowViewModel>());
    }

    [Theory]
    [InlineData(typeof(LoginViewModel))]
    [InlineData(typeof(DashboardViewModel))]
    [InlineData(typeof(CheckUpViewModel))]
    [InlineData(typeof(HistoryViewModel))]
    [InlineData(typeof(MessagesViewModel))]
    [InlineData(typeof(ProfileViewModel))]
    public void Every_page_can_be_navigated_to(Type pageType)
    {
        ServiceCollection services = new();
        services.AddHeartbeatDesktop();
        using ServiceProvider provider = services.BuildServiceProvider();

        PageViewModel page = provider.GetRequiredService<INavigationService>().NavigateTo(pageType);

        Assert.IsType(pageType, page);
        Assert.False(string.IsNullOrWhiteSpace(page.Title));
    }

    [Fact]
    public void Main_window_is_registered_as_a_singleton()
    {
        ServiceCollection services = new();
        services.AddHeartbeatDesktop();

        ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == typeof(MainWindow));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
