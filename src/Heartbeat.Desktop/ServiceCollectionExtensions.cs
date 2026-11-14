using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra la ventana principal, la navegación, los servicios de la barra de estado y todas las pantallas.</summary>
    public static IServiceCollection AddHeartbeatDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Base de datos local (SQLite) para funcionamiento offline.
        services.AddDbContext<LocalDbContext>(options =>
            options.UseSqlite("Data Source=heartbeat-desktop.db"));

        // Infraestructura de la interfaz.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IConnectivityService, NetworkConnectivityService>();
        services.AddSingleton<ISyncStatusProvider, NoSyncStatusProvider>();

        // Ventana principal.
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        // Pantallas: transitorias, se crean (y liberan) en cada navegación; ver NavigationService.
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CheckUpViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<MessagesViewModel>();
        services.AddTransient<ProfileViewModel>();

        return services;
    }
}
