using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public void Lists_only_the_check_ups_of_the_signed_in_astronaut_newest_first()
    {
        TestWorld world = new("ana");

        HistoryViewModel history = new(world.Store, world.Session, world.Info);

        Assert.Equal(3, history.CheckUps.Count);
        Assert.True(history.HasItems);
        Assert.All(history.CheckUps, row => Assert.Equal(6, row.Readings.Count));
        // El de hace dos días tenía la frecuencia cardíaca alta y poco sueño: vigilancia; los otros, normal.
        Assert.Equal(
            [HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Normal],
            history.CheckUps.Select(r => r.Status));
    }

    [Fact]
    public void Another_astronaut_sees_only_their_own_data()
    {
        TestWorld world = new("mara");

        HistoryViewModel history = new(world.Store, world.Session, world.Info);

        Assert.Single(history.CheckUps);
    }

    [Fact]
    public void Without_a_session_the_history_is_empty()
    {
        TestWorld world = new(signedInUser: null);

        HistoryViewModel history = new(world.Store, world.Session, world.Info);

        Assert.Empty(history.CheckUps);
        Assert.False(history.HasItems);
    }

    [Fact]
    public void Seeded_check_ups_are_synced_and_new_offline_ones_show_as_pending_until_the_link_returns()
    {
        TestWorld world = new("mara", online: false);
        HistoryViewModel history = new(world.Store, world.Session, world.Info);
        Assert.All(history.CheckUps, row => Assert.Equal("Sincronizado", row.SyncText));

        world.Store.AddCheckUp(TestWorld.IdOf("mara"), [new(IndicatorType.HeartRate, 70)], world.Evaluator.Evaluate([new ReadingInput(IndicatorType.HeartRate, 70)]));

        Assert.Equal("Pendiente de sincronizar", history.CheckUps[0].SyncText);

        world.Connectivity.Set(true);

        Assert.Equal("Sincronizado", history.CheckUps[0].SyncText);
    }

    [Fact]
    public void Disposing_stops_following_the_store()
    {
        TestWorld world = new("mara");
        HistoryViewModel history = new(world.Store, world.Session, world.Info);
        history.Dispose();

        world.Store.AddCheckUp(TestWorld.IdOf("mara"), [new(IndicatorType.HeartRate, 70)], world.Evaluator.Evaluate([new ReadingInput(IndicatorType.HeartRate, 70)]));

        Assert.Single(history.CheckUps);
    }
}
