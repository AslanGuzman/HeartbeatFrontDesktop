using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra la ventana principal, la navegación, los servicios y todas las pantallas.</summary>
    public static IServiceCollection AddHeartbeatDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Base de datos local (SQLite) para funcionamiento offline.
        services.AddDbContext<LocalDbContext>(options =>
            options.UseSqlite(LocalDatabase.ConnectionString(LocalDatabase.DefaultPath)));

        // Infraestructura de la interfaz.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISessionContext, SessionContext>();

        // Conectividad: la red real más el interruptor "Enlace con la Tierra" para demostrar el modo offline.
        services.AddSingleton<NetworkConnectivityService>();
        services.AddSingleton<SimulatedLinkService>();
        services.AddSingleton<IConnectivityService>(sp => sp.GetRequiredService<SimulatedLinkService>());
        services.AddSingleton<ISimulatedLink>(sp => sp.GetRequiredService<SimulatedLinkService>());

        // Reglas de evaluación compartidas con el backend (Heartbeat.Shared).
        services.AddSingleton<IHealthEvaluator, HealthEvaluator>();

        // Check-ups y mensajes se guardan en la base local (#8). Provisionales: el acceso de demostración (#27) y la
        // sincronización simulada (#32), que se sustituyen registrando las implementaciones reales en su lugar.
        services.AddSingleton<IAuthenticator, DemoAuthenticator>();
        services.AddSingleton<IHealthRecordStore, LocalHealthRecordStore>();
        services.AddSingleton<ISyncStatusProvider, DemoSyncStatusProvider>();
        services.AddSingleton<IBodyMeasurementStore, DemoBodyMeasurementStore>();
        services.AddTransient<BodyMeasurementsViewModel>();

        // Panel de información y fuentes por indicador (DK-INFO).
        services.AddSingleton<IIndicatorInfoProvider, ReferenceRangesInfoProvider>();
        services.AddSingleton<IUrlLauncher, ShellUrlLauncher>();
        services.AddSingleton<IIndicatorInfoPresenter, IndicatorInfoWindowPresenter>();
        services.AddTransient<IndicatorInfoViewModel>();

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
