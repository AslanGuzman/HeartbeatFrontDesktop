using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Los servicios de las pantallas montados a mano: reglas reales de Shared, almacén en memoria y reloj fijo.</summary>
internal sealed class TestWorld : IDisposable
{
    private readonly TestDatabase _database = new();
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public TestWorld(string? signedInUser = "ana", bool online = true, bool demoSync = true)
    {
        Clock = new FixedTimeProvider(Now);
        Evaluator = new HealthEvaluator();
        Info = new ReferenceRangesInfoProvider();

        // Base SQLite real en memoria (la misma que usa la app), con el historial de demostración sembrado.
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IHealthEvaluator>(Evaluator);
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite(_database.ConnectionString));
        services.AddSingleton<IEnvironmentReadingStore, LocalEnvironmentReadingStore>();
        Services = services.BuildServiceProvider();
        SeedAccounts();
        DemoDataSeeder.Seed(Services);
        ProfileStore = NewProfileStore(new ImmediateSynchronizationContext());
        EnvironmentStore = Services.GetRequiredService<IEnvironmentReadingStore>();
        Store = NewStore(new ImmediateSynchronizationContext());
        Connectivity = new FakeConnectivityService(online);
        Presenter = new FakeIndicatorInfoPresenter();
        Session = new SessionContext();
        ReminderSettings = new FakeReminderSettings(CheckUpReminderSettings.Disabled);
        Notification = new FakeCheckUpNotification();
        Reminder = new CheckUpReminder(ReminderSettings, Store, Session, Notification, Clock);
        Sync = demoSync ? new InstantSyncStatusProvider(Store, ProfileStore, Connectivity, Clock) : null!;

        Navigation = new RecordingNavigation(type =>
            type == typeof(CheckUpResultViewModel) ? new CheckUpResultViewModel(Navigation!, Store, Session, Info, Presenter, Connectivity)
            : type == typeof(CheckUpViewModel) ? NewCheckUp()
            : type == typeof(DashboardViewModel) ? new DashboardViewModel(Store, Session, Info, Presenter, Navigation!, Clock, Reminder, ProfileStore, EnvironmentStore)
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

    public LocalProfileStore ProfileStore { get; }

    public IEnvironmentReadingStore EnvironmentStore { get; }

    public LocalProfileStore NewProfileStore(SynchronizationContext? ui) =>
        new(Services.GetRequiredService<IServiceScopeFactory>(), Clock, ui);

    /// <summary>Perfil del servidor tal como quedaría guardado tras el primer acceso en línea de un astronauta de demostración.</summary>
    public static AstronautProfile ServerProfile(string userName, DateTimeOffset? updatedAt = null)
    {
        SessionUser account = DemoAccounts.All.First(a => a.UserName == userName);
        return new AstronautProfile(
            account.Id, account.UserName, account.DisplayName, "Comet",
            new MissionInfo(
                Guid.Parse("0197c2a0-0000-7000-8000-0000000000ff"),
                "Artemis Demo 1",
                "station",
                Now.AddDays(-100),
                new EnvironmentProfile("station", 0m, 408m, 4, 388m)),
            new DateOnly(1988, 3, 12), "Alergia a la penicilina", updatedAt ?? Now.AddDays(-10));
    }

    // Una cuenta local por astronauta de demostración, con su perfil guardado (como tras el primer acceso en línea).
    private void SeedAccounts()
    {
        using IServiceScope scope = Services.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        foreach (SessionUser account in DemoAccounts.All)
        {
            db.LocalAccounts.Add(new LocalAccount
            {
                AstronautId = account.Id,
                UserName = account.UserName,
                PasswordHash = "1.AAAA",
                Salt = "AAAA",
                ProfileJson = ProfileCache.Write(new CachedProfile(ServerProfile(account.UserName), null)),
            });
        }

        db.SaveChanges();
    }

    public static Guid IdOf(string userName) => DemoAccounts.All.First(a => a.UserName == userName).Id;

    public LocalHealthRecordStore NewStore(SynchronizationContext? ui) =>
        new(Services.GetRequiredService<IServiceScopeFactory>(), Evaluator, Clock, ui);

    public void Dispose()
    {
        Services.Dispose();
        _database.Dispose();
    }

    public FakeConnectivityService Connectivity { get; }

    public FakeIndicatorInfoPresenter Presenter { get; }

    public SessionContext Session { get; }

    public FakeReminderSettings ReminderSettings { get; }

    public FakeCheckUpNotification Notification { get; }

    public CheckUpReminder Reminder { get; }

    public InstantSyncStatusProvider Sync { get; }
}
