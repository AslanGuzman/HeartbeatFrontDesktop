using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Los servicios de las pantallas montados a mano: reglas reales de Shared, almacén en memoria y reloj fijo.</summary>
internal sealed class TestWorld : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public TestWorld(string? signedInUser = "ana", bool online = true)
    {
        Clock = new FixedTimeProvider(Now);
        Evaluator = new HealthEvaluator();
        Info = new ReferenceRangesInfoProvider();

        // Base SQLite real en memoria (la misma que usa la app), con el historial de demostración sembrado.
        _connection.Open();
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IHealthEvaluator>(Evaluator);
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite(_connection));
        Services = services.BuildServiceProvider();
        using (IServiceScope scope = Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<LocalDbContext>().Database.EnsureCreated();
        }

        DemoDataSeeder.Seed(Services);
        Store = NewStore(new ImmediateSynchronizationContext());
        Connectivity = new FakeConnectivityService(online);
        Presenter = new FakeIndicatorInfoPresenter();
        Session = new SessionContext();
        Sync = new DemoSyncStatusProvider(Store, Connectivity, Clock);

        Navigation = new RecordingNavigation(type =>
            type == typeof(CheckUpResultViewModel) ? new CheckUpResultViewModel(Navigation!, Store, Session, Info, Presenter, Connectivity)
            : type == typeof(CheckUpViewModel) ? NewCheckUp()
            : type == typeof(DashboardViewModel) ? new DashboardViewModel(Store, Session, Info, Presenter, Navigation!, Clock)
            : type == typeof(HistoryViewModel) ? new HistoryViewModel(Store, Session, Info)
            : new PlainPageViewModel());

        if (signedInUser is not null)
        {
            Session.SignIn(DemoAccounts.All.First(a => a.UserName == signedInUser));
        }
    }

    public FixedTimeProvider Clock { get; }

    public HealthEvaluator Evaluator { get; }

    public ReferenceRangesInfoProvider Info { get; }

    public RecordingNavigation Navigation { get; }

    /// <summary>El resultado del último check-up guardado (la pantalla a la que se navegó).</summary>
    public CheckUpResultViewModel Result => (CheckUpResultViewModel)Navigation.CurrentPage!;

    public CheckUpViewModel NewCheckUp() =>
        new(Store, Session, Evaluator, Info, Presenter, Navigation,
            new BodyMeasurementsViewModel(new Heartbeat.Desktop.Demo.DemoBodyMeasurementStore(Clock), Session));

    public ServiceProvider Services { get; }

    public LocalHealthRecordStore Store { get; }

    public static Guid IdOf(string userName) => DemoAccounts.All.First(a => a.UserName == userName).Id;

    public LocalHealthRecordStore NewStore(SynchronizationContext? ui) =>
        new(Services.GetRequiredService<IServiceScopeFactory>(), Evaluator, Clock, ui);

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }

    public FakeConnectivityService Connectivity { get; }

    public FakeIndicatorInfoPresenter Presenter { get; }

    public SessionContext Session { get; }

    public DemoSyncStatusProvider Sync { get; }
}
