using System.Net.Http;

using Heartbeat.Client;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Sync;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Heartbeat.Desktop.Tests;

/// <summary>El ciclo en segundo plano: cuándo se dispara una sincronización sin que nadie la pida.</summary>
public sealed class SyncBackgroundTests
{
    private static DateTimeOffset T(int minutes) => TestWorld.Now.AddMinutes(minutes);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 4000)
    {
        DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < limit)
        {
            await Task.Delay(10);
        }
    }

    private static async Task Settle() => await Task.Delay(150);

    [Fact]
    public async Task Starting_the_app_with_a_session_syncs_right_away()
    {
        using SyncTestWorld w = new();
        w.AddCheckUp("ana", T(-1));

        await w.Engine.StartAsync(CancellationToken.None);
        await WaitUntil(() => w.World.Store.GetPendingCount(w.Id("ana")) == 0);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));
        Assert.NotEmpty(w.Api.Syncs);
    }

    [Fact]
    public async Task Saving_a_check_up_triggers_a_sync_without_anyone_asking()
    {
        using SyncTestWorld w = new();
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();
        int before = w.Api.Syncs.Count;

        CheckUpRecord saved = w.AddCheckUp("ana", T(-1));
        await WaitUntil(() => w.CheckUps().Single(c => c.Id == saved.Id).SyncState == SyncState.Synced);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(SyncState.Synced, w.CheckUps().Single(c => c.Id == saved.Id).SyncState);
        Assert.True(w.Api.Syncs.Count > before);
    }

    [Fact]
    public async Task Saving_a_message_or_a_profile_edit_also_triggers_a_sync()
    {
        using SyncTestWorld w = new();
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();

        w.World.Store.QueueMessage(w.Id("ana"), "hola");
        await WaitUntil(() => w.World.Store.GetPendingCount(w.Id("ana")) == 0);
        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));

        w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(2), profile: req.Profile is null ? null : TestWorld.ServerProfile("ana", req.Profile.UpdatedAt)));
        w.World.ProfileStore.Save(w.Id("ana"), "Ana R.", null, null);
        await WaitUntil(() => w.World.ProfileStore.GetPendingEdit(w.Id("ana")) is null);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Null(w.World.ProfileStore.GetPendingEdit(w.Id("ana")));
    }

    // La prueba manual del ticket: desconectar la red, guardar 3 check-ups, reconectar, se suben.
    [Fact]
    public async Task Disconnect_save_three_check_ups_reconnect_and_they_are_uploaded()
    {
        using SyncTestWorld w = new(online: false);
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();

        w.AddCheckUp("ana", T(-3));
        w.AddCheckUp("ana", T(-2));
        w.AddCheckUp("ana", T(-1));
        await Settle();
        Assert.Empty(w.Api.Syncs);
        Assert.Equal(3, w.Engine.PendingCount);

        w.World.Connectivity.Set(true);
        await WaitUntil(() => w.Engine.PendingCount == 0);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(0, w.Engine.PendingCount);
        Assert.Equal([3], w.Api.Syncs.Select(s => s.CheckUps.Count).Where(c => c > 0));
        Assert.All(w.CheckUps().Where(c => c.AstronautId == w.Id("ana") && c.RecordedAt >= T(-3)), c => Assert.Equal(SyncState.Synced, c.SyncState));
    }

    [Fact]
    public async Task Signing_in_triggers_a_sync_of_that_astronauts_pending_items()
    {
        using SyncTestWorld w = new(user: null);
        w.AddCheckUp("luis", T(-1));
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();
        Assert.Empty(w.Api.Syncs);

        w.World.Session.SignIn(DemoAccounts.All.First(a => a.UserName == "luis"));
        await WaitUntil(() => w.World.Store.GetPendingCount(w.Id("luis")) == 0);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("luis")));
    }

    [Fact]
    public async Task It_syncs_every_interval_while_there_is_a_link()
    {
        using SyncTestWorld w = new(interval: TimeSpan.FromMilliseconds(60));

        await w.Engine.StartAsync(CancellationToken.None);
        await WaitUntil(() => w.Api.Syncs.Count >= 4);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.True(w.Api.Syncs.Count >= 4, $"solo hubo {w.Api.Syncs.Count} sincronizaciones");
    }

    [Fact]
    public async Task Without_a_link_the_periodic_ticks_do_not_call_the_server()
    {
        using SyncTestWorld w = new(online: false, interval: TimeSpan.FromMilliseconds(40));

        await w.Engine.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Empty(w.Api.Syncs);
    }

    [Fact]
    public async Task A_failing_cycle_does_not_stop_the_next_ones()
    {
        using SyncTestWorld w = new(interval: TimeSpan.FromMilliseconds(50));
        w.AddCheckUp("ana", T(-1));
        int calls = 0;
        w.Api.OnSyncAsync = req => ++calls <= 2
            ? throw new HttpRequestException("caído")
            : Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1)));

        await w.Engine.StartAsync(CancellationToken.None);
        await WaitUntil(() => w.World.Store.GetPendingCount(w.Id("ana")) == 0);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));
        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task An_unexpected_exception_inside_a_cycle_does_not_kill_the_loop()
    {
        using SyncTestWorld w = new(interval: TimeSpan.FromMilliseconds(50));
        int calls = 0;
        w.Api.OnSyncAsync = req => ++calls == 1
            ? throw new InvalidProgramException("fallo raro")
            : Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1)));

        await w.Engine.StartAsync(CancellationToken.None);
        await WaitUntil(() => calls >= 3);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task A_burst_of_saves_is_grouped_into_few_syncs_and_everything_is_uploaded()
    {
        using SyncTestWorld w = new();
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();
        int before = w.Api.Syncs.Count;

        for (int i = 0; i < 12; i++)
        {
            w.AddCheckUp("ana", T(-30 + i));
        }

        await WaitUntil(() => w.World.Store.GetPendingCount(w.Id("ana")) == 0);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));
        Assert.Equal(12, w.Api.Syncs.Skip(before).Sum(s => s.CheckUps.Count));
    }

    [Fact]
    public async Task Stopping_ends_the_loop_promptly()
    {
        using SyncTestWorld w = new(interval: TimeSpan.FromMinutes(10));
        await w.Engine.StartAsync(CancellationToken.None);
        await Settle();

        Task stop = w.Engine.StopAsync(CancellationToken.None);
        Task finished = await Task.WhenAny(stop, Task.Delay(3000));

        Assert.Same(stop, finished);
    }

    [Fact]
    public async Task Signing_out_stops_syncing_for_that_astronaut()
    {
        using SyncTestWorld w = new(interval: TimeSpan.FromMilliseconds(50));
        await w.Engine.StartAsync(CancellationToken.None);
        await WaitUntil(() => w.Api.Syncs.Count >= 2);
        w.World.Session.SignOut();
        await Settle();
        int after = w.Api.Syncs.Count;

        await Task.Delay(300);
        await w.Engine.StopAsync(CancellationToken.None);

        Assert.Equal(after, w.Api.Syncs.Count);
    }

    [Fact]
    public async Task The_engine_tells_the_status_bar_when_things_change()
    {
        using SyncTestWorld w = new();
        int changes = 0;
        w.Engine.Changed += (_, _) => Interlocked.Increment(ref changes);

        w.AddCheckUp("ana", T(-1));
        await w.Engine.SyncNowAsync();

        Assert.True(changes >= 2);
    }
}

public sealed class SyncWiringTests
{
    [Fact]
    public void The_engine_is_the_hosted_service_the_status_provider_and_the_sync_engine()
    {
        using TestWorldContainer c = new();

        SyncEngine engine = c.Provider.GetRequiredService<SyncEngine>();

        Assert.Same(engine, c.Provider.GetRequiredService<ISyncEngine>());
        Assert.Same(engine, c.Provider.GetRequiredService<ISyncStatusProvider>());
        Assert.Contains(c.Provider.GetServices<IHostedService>(), h => ReferenceEquals(h, engine));
    }

    [Fact]
    public void The_evaluator_and_the_information_use_the_same_ranges()
    {
        using TestWorldContainer c = new();

        RangesSource ranges = c.Provider.GetRequiredService<RangesSource>();

        Assert.Equal(ranges.InUseVersion, c.Provider.GetRequiredService<IHealthEvaluator>().RangesVersion);
        Assert.Equal(ranges.InUseVersion, c.Provider.GetRequiredService<Heartbeat.Desktop.Info.IIndicatorInfoProvider>().RangesVersion);
    }
}

internal sealed class TestWorldContainer : IDisposable
{
    private readonly TestDatabase _database;

    public TestWorldContainer()
    {
        ServiceCollection services = TestContainer.Services(out _database);
        Provider = services.BuildServiceProvider();
    }

    public ServiceProvider Provider { get; }

    public void Dispose()
    {
        Provider.Dispose();
        _database.Dispose();
    }
}

public sealed class RangesSourceTests : IDisposable
{
    private readonly TestWorld _world = new("ana");

    public void Dispose() => _world.Dispose();

    private static string RangesJson(string version) => ReferenceRanges.Json.Replace(ReferenceRanges.Version, version);

    private void Cache(string version, string json)
    {
        using IServiceScope scope = _world.Services.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.RangesCache.Add(new RangesCache { Id = 1, Version = version, Json = json });
        db.SaveChanges();
    }

    [Fact]
    public void Without_a_cache_the_embedded_ranges_are_used()
    {
        RangesSource source = RangesSource.Load(_world.Services);

        Assert.Equal(ReferenceRanges.Version, source.InUseVersion);
        Assert.Equal(ReferenceRanges.Json, source.Json);
        Assert.False(source.RestartPending);
    }

    [Fact]
    public void Newer_valid_ranges_from_a_previous_sync_are_used_at_startup()
    {
        string newer = RangesJson("2099.01.01-1");
        Cache("2099.01.01-1", newer);

        RangesSource source = RangesSource.Load(_world.Services);

        Assert.Equal("2099.01.01-1", source.InUseVersion);
        Assert.Equal(newer, source.Json);
        Assert.Equal("2099.01.01-1", new HealthEvaluator(source.Json).RangesVersion);
    }

    [Theory]
    [InlineData("2000.01.01-1")]
    [InlineData("")]
    public void Older_or_empty_cached_ranges_are_ignored(string version)
    {
        Cache(version, RangesJson(version.Length == 0 ? "x" : version));

        Assert.Equal(ReferenceRanges.Version, RangesSource.Load(_world.Services).InUseVersion);
    }

    [Fact]
    public void Damaged_cached_json_falls_back_to_the_embedded_ranges()
    {
        Cache("2099.01.01-1", "{ no es json");

        Assert.Equal(ReferenceRanges.Version, RangesSource.Load(_world.Services).InUseVersion);
    }

    [Fact]
    public void A_cache_whose_row_version_disagrees_with_its_json_is_ignored()
    {
        Cache("2099.01.01-1", RangesJson("2098.01.01-1"));

        Assert.Equal(ReferenceRanges.Version, RangesSource.Load(_world.Services).InUseVersion);
    }

    [Fact]
    public void Json_that_parses_but_is_not_usable_ranges_is_ignored()
    {
        Cache("2099.01.01-1", "{ \"version\": \"2099.01.01-1\", \"sources\": {}, \"indicators\": [] }");

        Assert.Equal(ReferenceRanges.Version, RangesSource.Load(_world.Services).InUseVersion);
    }

    [Fact]
    public void Without_an_initialised_database_the_embedded_ranges_are_used()
    {
        ServiceCollection services = new();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(ReferenceRanges.Version, RangesSource.Load(provider).InUseVersion);
    }

    [Fact]
    public void Marking_new_ranges_as_downloaded_only_changes_what_is_reported_to_the_server()
    {
        RangesSource source = RangesSource.Load(_world.Services);

        source.MarkDownloaded("2099.01.01-1");

        Assert.Equal(ReferenceRanges.Version, source.InUseVersion);
        Assert.Equal("2099.01.01-1", source.LatestVersion);
        Assert.True(source.RestartPending);
    }

    [Fact]
    public void Validation_accepts_the_embedded_ranges_and_rejects_garbage()
    {
        Assert.True(RangesSource.TryValidate(ReferenceRanges.Json, out string? version));
        Assert.Equal(ReferenceRanges.Version, version);
        Assert.False(RangesSource.TryValidate("", out _));
        Assert.False(RangesSource.TryValidate("[]", out _));
        Assert.False(RangesSource.TryValidate("{ \"version\": \"1\" }", out _));
    }
}
