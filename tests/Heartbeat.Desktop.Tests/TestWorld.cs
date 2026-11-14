using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

/// <summary>Los servicios de las pantallas montados a mano: reglas reales de Shared, almacén en memoria y reloj fijo.</summary>
internal sealed class TestWorld
{
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public TestWorld(string? signedInUser = "ana", bool online = true)
    {
        Clock = new FixedTimeProvider(Now);
        Evaluator = new HealthEvaluator();
        Info = new ReferenceRangesInfoProvider();
        Store = new DemoHealthRecordStore(Evaluator, Clock, new ImmediateSynchronizationContext());
        Connectivity = new FakeConnectivityService(online);
        Presenter = new FakeIndicatorInfoPresenter();
        Session = new SessionContext();
        Sync = new DemoSyncStatusProvider(Store, Connectivity, Clock);

        if (signedInUser is not null)
        {
            Session.SignIn(DemoAuthenticator.Accounts.First(a => a.UserName == signedInUser));
        }
    }

    public FixedTimeProvider Clock { get; }

    public HealthEvaluator Evaluator { get; }

    public ReferenceRangesInfoProvider Info { get; }

    public DemoHealthRecordStore Store { get; }

    public FakeConnectivityService Connectivity { get; }

    public FakeIndicatorInfoPresenter Presenter { get; }

    public SessionContext Session { get; }

    public DemoSyncStatusProvider Sync { get; }
}
