using System.Net.Http;
using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Client;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class DemoServicesTests
{
    [Fact]
    public void The_store_starts_synced_with_a_last_sync_time()
    {
        TestWorld world = new();

        Assert.Equal(0, world.Store.PendingCount);
        Assert.NotNull(world.Store.LastSyncedAt);
        Assert.Equal(world.Store.LastSyncedAt, world.Sync.LastSyncAt);
    }

    [Fact]
    public void Offline_new_items_accumulate_as_pending_and_the_sync_status_reports_them()
    {
        TestWorld world = new(online: false);
        int changes = 0;
        world.Sync.Changed += (_, _) => changes++;

        world.Store.QueueMessage(TestWorld.IdOf("ana"), "uno");
        world.Store.QueueMessage(TestWorld.IdOf("ana"), "dos");

        Assert.Equal(2, world.Sync.PendingCount);
        Assert.True(changes >= 2);
    }

    [Fact]
    public void Regaining_the_link_sends_everything_pending_and_updates_the_last_sync()
    {
        TestWorld world = new(online: false);
        world.Store.QueueMessage(TestWorld.IdOf("ana"), "uno");
        world.Clock.Now = world.Clock.Now.AddHours(1);

        world.Connectivity.Set(true);

        Assert.Equal(0, world.Sync.PendingCount);
        Assert.Equal(world.Clock.Now, world.Sync.LastSyncAt);
    }

    [Fact]
    public void Online_new_items_are_sent_right_away()
    {
        TestWorld world = new(online: true);

        world.Store.QueueMessage(TestWorld.IdOf("ana"), "uno");

        Assert.Equal(0, world.Sync.PendingCount);
    }

    [Fact]
    public void Each_astronaut_only_sees_their_own_records()
    {
        TestWorld world = new();

        Assert.Equal(3, world.Store.GetCheckUps(TestWorld.IdOf("ana")).Count);
        Assert.Equal(2, world.Store.GetCheckUps(TestWorld.IdOf("luis")).Count);
        Assert.Single(world.Store.GetCheckUps(TestWorld.IdOf("mara")));
        Assert.Empty(world.Store.GetCheckUps(Guid.NewGuid()));
    }

    [Fact]
    public void Seed_check_ups_use_the_shared_evaluator_and_cover_the_three_states()
    {
        TestWorld world = new();

        List<HealthStatus> seen = new[] { "ana", "luis", "mara" }
            .SelectMany(u => world.Store.GetCheckUps(TestWorld.IdOf(u)))
            .Select(c => c.OverallStatus)
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal([HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Critical], seen);
    }

    [Fact]
    public void Changed_is_raised_through_the_ui_context()
    {
        QueueingSynchronizationContext ui = new();
        TestWorld world = new();
        LocalHealthRecordStore store = world.NewStore(ui);
        int raised = 0;
        store.Changed += (_, _) => raised++;

        store.QueueMessage(TestWorld.IdOf("ana"), "uno");

        Assert.Equal(0, raised);
        ui.RunPending();
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task The_demo_api_accepts_only_the_demo_accounts_with_the_demo_password_and_keeps_stable_ids()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));

        CrewLoginResponse ok = await api.LoginAsync(new LoginRequest("Ana", "demo123", "equipo"));
        CrewLoginResponse again = await api.LoginAsync(new LoginRequest("ana", "demo123", "equipo"));

        Assert.Equal("Ana Rivera", ok.Profile.DisplayName);
        Assert.Equal(ok.Profile.Id, again.Profile.Id);
        Assert.Equal(TestWorld.IdOf("ana"), ok.Profile.Id);
        Assert.Equal("demo-access-token", api.AccessToken);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(() => api.LoginAsync(new LoginRequest("ana", "otra", "equipo")));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Session_raises_changed_on_sign_in_and_out()
    {
        SessionContext session = new();
        int changes = 0;
        session.Changed += (_, _) => changes++;

        session.SignIn(new SessionUser(TestWorld.IdOf("ana"), "ana", "Ana Rivera"));
        Assert.Equal("ana", session.CurrentUser!.UserName);
        session.SignOut();

        Assert.Null(session.CurrentUser);
        Assert.Equal(2, changes);
    }
}

public sealed class SimulatedLinkServiceTests
{
    [Fact]
    public void Turning_the_link_off_always_means_offline_and_turning_it_on_follows_the_real_network()
    {
        using NetworkConnectivityService network = new();
        using SimulatedLinkService link = new(network);

        link.IsLinkEnabled = false;
        Assert.False(link.IsOnline);

        link.IsLinkEnabled = true;
        Assert.Equal(network.IsOnline, link.IsOnline);
    }

    [Fact]
    public void Raises_the_event_only_when_the_effective_state_changes()
    {
        using NetworkConnectivityService network = new();
        using SimulatedLinkService link = new(network);
        int raised = 0;
        link.IsOnlineChanged += (_, _) => raised++;

        link.IsLinkEnabled = false;
        link.IsLinkEnabled = false;
        link.IsLinkEnabled = true;

        // Sin red real el estado efectivo nunca cambia, así que no hay eventos; con red hay dos.
        Assert.Equal(network.IsOnline ? 2 : 0, raised);
    }
}

public sealed class StatusPaletteTests
{
    [Fact]
    public void The_three_states_have_distinct_colors_and_spanish_names()
    {
        HealthStatus[] all = [HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Critical];

        Assert.Equal(3, all.Select(s => StatusPalette.Dot(s).ToString()).Distinct().Count());
        Assert.Equal(3, all.Select(s => StatusPalette.Soft(s).ToString()).Distinct().Count());
        Assert.Equal(["Normal", "Vigilancia", "Crítico"], all.Select(s => StatusPalette.Name(s)));
    }

    [Fact]
    public void No_data_is_neither_green_amber_nor_red()
    {
        Assert.Equal("Sin datos", StatusPalette.Name(null));
        foreach (HealthStatus status in new[] { HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Critical })
        {
            Assert.NotEqual(StatusPalette.Dot(status).ToString(), StatusPalette.Dot(null).ToString());
        }
    }

    [Fact]
    public void The_converter_picks_the_variant_from_its_parameter()
    {
        StatusToBrushConverter converter = new();

        Assert.Equal(StatusPalette.Dot(HealthStatus.Watch), converter.Convert(HealthStatus.Watch, typeof(object), null, null!));
        Assert.Equal(StatusPalette.Text(HealthStatus.Watch), converter.Convert(HealthStatus.Watch, typeof(object), "Text", null!));
        Assert.Equal(StatusPalette.Soft(HealthStatus.Watch), converter.Convert(HealthStatus.Watch, typeof(object), "Soft", null!));
        Assert.Equal(StatusPalette.Dot(null), converter.Convert(null, typeof(object), null, null!));
    }
}
