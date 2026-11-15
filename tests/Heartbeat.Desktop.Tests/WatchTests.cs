using System.IO;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Live;
using Heartbeat.Desktop.Salud;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Watch;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

internal sealed class FakeHeartRateFeed : IHeartRateFeed
{
    public WatchFeedState State { get; set; } = WatchFeedState.Stopped;

    public List<HeartRateReading> Recent { get; } = [];

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler<HeartRateReading>? ReadingReceived;

    public event EventHandler? StateChanged;

    public Task<IReadOnlyList<HeartRateReading>> LoadRecentAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HeartRateReading>>(Recent.ToList());

    public void Start()
    {
        Starts++;
        State = WatchFeedState.Live;
    }

    public void Stop()
    {
        Stops++;
        State = WatchFeedState.Stopped;
    }

    public void Send(HeartRateReading reading) => ReadingReceived?.Invoke(this, reading);

    public void Change(WatchFeedState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeSamsungHealthClient(SamsungHealthData data) : ISamsungHealthClient
{
    public int Calls { get; private set; }

    public Task<SamsungHealthData> LoadAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(data);
    }
}

internal sealed class InMemoryLastWatchStore : ILastWatchReadingStore
{
    public Dictionary<Guid, HeartRateReading> Saved { get; } = [];

    public HeartRateReading? Get(Guid astronautId) => Saved.GetValueOrDefault(astronautId);

    public void Save(Guid astronautId, HeartRateReading reading) => Saved[astronautId] = reading;
}

/// <summary>El reloj en la app (WR-DESKTOP): FC en vivo con el token, última lectura sin enlace y aviso de datos no validados.</summary>
public sealed class WatchViewModelTests
{
    private static readonly SamsungHealthData EmptyHealth = new(null, null, null, null, []);

    private readonly TestWorld _world = new("ana");
    private readonly FakeHeartRateFeed _feed = new();
    private readonly InMemoryLastWatchStore _store = new();
    private readonly FakeConnectivityService _connectivity;

    public WatchViewModelTests() => _connectivity = new FakeConnectivityService(true);

    private WatchViewModel Create(bool online = true, SamsungHealthData? health = null)
    {
        FakeConnectivityService connectivity = online ? _connectivity : new FakeConnectivityService(false);
        return new WatchViewModel(_feed, new FakeSamsungHealthClient(health ?? EmptyHealth), _store, _world.Session, connectivity, _world.Clock, new ImmediateSynchronizationContext());
    }

    private static HeartRateReading Reading(int bpm, DateTimeOffset at) => new(bpm, at, "galaxy-watch-5");

    [Fact]
    public void The_not_clinically_validated_notice_is_always_there()
    {
        Assert.Equal("Datos del reloj, no validados clínicamente", Create().DisclaimerText);
    }

    [Fact]
    public void It_is_a_menu_section_and_not_a_loose_window()
    {
        ServiceCollection services = TestContainer.Services(out _);
        services.AddSingleton<Heartbeat.Desktop.Services.IConnectivityService>(new FakeConnectivityService());
        using ServiceProvider provider = services.BuildServiceProvider();

        MainWindowViewModel window = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Contains(window.NavigationItems, i => i.PageType == typeof(WatchViewModel));
    }

    [Fact]
    public void A_reading_shows_the_bpm_and_counts_as_live()
    {
        using WatchViewModel watch = Create();

        _feed.Send(Reading(77, TestWorld.Now));

        Assert.Equal("77", watch.BpmText);
        Assert.True(watch.IsFresh);
        Assert.False(watch.ShowNoDataNotice);
        Assert.Equal("en vivo", watch.StatusText);
        Assert.Equal([77], watch.Samples);
    }

    [Fact]
    public void It_connects_with_the_session_and_starts_the_feed_when_there_is_a_link()
    {
        using WatchViewModel watch = Create();

        Assert.Equal(1, _feed.Starts);
    }

    [Fact]
    public void Without_a_link_it_shows_the_last_saved_reading_with_its_time_and_the_no_data_notice()
    {
        _store.Saved[TestWorld.IdOf("ana")] = Reading(72, new DateTimeOffset(2026, 11, 13, 9, 30, 0, TimeSpan.Zero));

        using WatchViewModel watch = Create(online: false);

        Assert.Equal(0, _feed.Starts);
        Assert.Equal("72", watch.BpmText);
        Assert.StartsWith("última lectura guardada", watch.MeasuredText);
        Assert.True(watch.ShowNoDataNotice);
        Assert.Contains("Sin datos del reloj", watch.NoDataNoticeText);
        Assert.Equal("sin enlace", watch.StatusText);
    }

    [Fact]
    public void With_no_saved_reading_and_no_link_there_is_just_the_notice()
    {
        using WatchViewModel watch = Create(online: false);

        Assert.Equal("--", watch.BpmText);
        Assert.Equal("Sin lecturas todavía", watch.MeasuredText);
        Assert.True(watch.ShowNoDataNotice);
    }

    [Fact]
    public void When_the_watch_stops_sending_the_notice_appears_after_ten_seconds()
    {
        using WatchViewModel watch = Create();
        _feed.Send(Reading(80, TestWorld.Now));
        Assert.False(watch.ShowNoDataNotice);

        _world.Clock.Now = TestWorld.Now.AddSeconds(11);
        watch.RefreshStatus();

        Assert.True(watch.ShowNoDataNotice);
        Assert.Contains("Sin datos del reloj", watch.NoDataNoticeText);
        Assert.Equal("sin datos hace más de 10 s", watch.StatusText);
        Assert.Equal("80", watch.BpmText);
    }

    [Fact]
    public void Losing_the_link_stops_the_feed_and_getting_it_back_starts_it_again()
    {
        FakeConnectivityService connectivity = new(true);
        using WatchViewModel watch = new(_feed, new FakeSamsungHealthClient(EmptyHealth), _store, _world.Session, connectivity, _world.Clock, new ImmediateSynchronizationContext());
        Assert.Equal(1, _feed.Starts);

        connectivity.Set(false);
        Assert.Equal(1, _feed.Stops);
        Assert.Equal("sin enlace", watch.StatusText);

        connectivity.Set(true);
        Assert.Equal(2, _feed.Starts);
    }

    [Fact]
    public void The_last_reading_is_saved_for_the_signed_in_astronaut_and_again_when_leaving()
    {
        WatchViewModel watch = Create();

        _feed.Send(Reading(81, TestWorld.Now));
        Assert.Equal(81, _store.Saved[TestWorld.IdOf("ana")].Bpm);

        _feed.Send(Reading(90, TestWorld.Now.AddSeconds(1)));
        watch.Dispose();

        Assert.Equal(90, _store.Saved[TestWorld.IdOf("ana")].Bpm);
        Assert.True(_feed.IsDisposed);
    }

    [Fact]
    public void Recent_history_fills_the_chart_when_it_opens()
    {
        _feed.Recent.AddRange([Reading(70, TestWorld.Now.AddSeconds(-2)), Reading(72, TestWorld.Now.AddSeconds(-1))]);

        using WatchViewModel watch = Create();

        Assert.Equal([70, 72], watch.Samples);
        Assert.Equal("72", watch.BpmText);
    }

    [Fact]
    public async Task The_samsung_health_tab_loads_on_first_open_and_shows_the_values()
    {
        SamsungHealthData data = new(
            new DailySummaryDto
            {
                Latest = new LatestValuesDto { HeartRate = new LatestValueDto { Value = 68, Unit = "lpm", MeasuredAt = TestWorld.Now } },
                Activity = new DailyActivityDto { Steps = 5400 },
            },
            [],
            [],
            [new SyncStatusDto { LastSyncedAt = TestWorld.Now.AddMinutes(-12) }],
            []);
        using WatchViewModel watch = Create(health: data);

        await watch.ShowHealthCommand.ExecuteAsync(null);

        Assert.True(watch.IsHealthTab);
        Assert.Equal("68 lpm", watch.HealthCards.First(c => c.Title == "Frecuencia cardíaca").Value);
        Assert.Equal("Sin datos", watch.HealthCards.First(c => c.Title == "Peso").Value);
        Assert.Contains("5", watch.StepsTodayText);
        Assert.Contains("hace 12 min", watch.SyncText);
        Assert.False(watch.HasHealthError);
    }

    [Fact]
    public async Task Samsung_health_needs_the_link_and_says_so_without_it()
    {
        using WatchViewModel watch = Create(online: false);

        await watch.ShowHealthCommand.ExecuteAsync(null);

        Assert.True(watch.HasHealthError);
        Assert.Contains("Sin enlace", watch.HealthErrorText);
    }

    [Fact]
    public async Task Errors_from_the_server_are_shown()
    {
        using WatchViewModel watch = Create(health: new SamsungHealthData(null, null, null, null, ["El servidor de datos de Samsung Health no responde"]));

        await watch.ShowHealthCommand.ExecuteAsync(null);

        Assert.Contains("no responde", watch.HealthErrorText);
    }

    [Fact]
    public void Changing_the_language_does_not_rebuild_the_page_or_cut_the_connection()
    {
        using WatchViewModel watch = Create();

        Assert.False(watch.RebuildsOnLanguageChange);
    }
}

/// <summary>La última lectura del reloj se recuerda por astronauta, en un fichero junto a la base local.</summary>
public sealed class LastWatchReadingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "heartbeat-watch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void A_reading_is_remembered_per_astronaut()
    {
        FileLastWatchReadingStore store = new(Path.Combine(_dir, "watch-last.json"));
        Guid ana = Guid.NewGuid();
        Guid luis = Guid.NewGuid();

        store.Save(ana, new HeartRateReading(72, TestWorld.Now, "galaxy-watch-5"));
        store.Save(luis, new HeartRateReading(64, TestWorld.Now.AddMinutes(1), null));

        FileLastWatchReadingStore reopened = new(Path.Combine(_dir, "watch-last.json"));
        Assert.Equal(72, reopened.Get(ana)!.Bpm);
        Assert.Equal(64, reopened.Get(luis)!.Bpm);
        Assert.Null(reopened.Get(Guid.NewGuid()));
    }

    [Fact]
    public void A_missing_or_broken_file_means_no_reading()
    {
        string path = Path.Combine(_dir, "watch-last.json");
        Assert.Null(new FileLastWatchReadingStore(path).Get(Guid.NewGuid()));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ no es json");
        Assert.Null(new FileLastWatchReadingStore(path).Get(Guid.NewGuid()));
    }
}

/// <summary>Icono de reloj en historial y gráficas para las lecturas con <c>source = wearable</c>.</summary>
public sealed class WatchReadingMarkerTests
{
    private static readonly IndicatorBands Bands = IndicatorBands.Parse(ReferenceRanges.Json);
    private static readonly HealthEvaluator Evaluator = new();

    private static CheckUpRecord CheckUp(int daysAgo, ReadingSource source, decimal bpm)
    {
        ReadingInput input = new(IndicatorType.HeartRate, bpm);
        return new CheckUpRecord(
            Guid.NewGuid(), TestWorld.Now.AddDays(-daysAgo), HealthStatus.Normal,
            [new ReadingRecord(input, Evaluator.Evaluate(input), source)]);
    }

    [Fact]
    public void A_reading_from_the_watch_is_marked_and_a_typed_one_is_not()
    {
        ReadingInput input = new(IndicatorType.HeartRate, 70);

        Assert.True(new ReadingRecord(input, Evaluator.Evaluate(input), ReadingSource.Wearable).FromWatch);
        Assert.False(new ReadingRecord(input, Evaluator.Evaluate(input)).FromWatch);
    }

    [Fact]
    public void The_chart_marks_the_points_that_come_from_the_watch()
    {
        ChartMetric metric = Bands.Metrics.First(m => m.Indicator == IndicatorType.HeartRate);

        TrendChartModel chart = TrendChartBuilder.Build(
            metric, "Frecuencia cardíaca",
            [CheckUp(2, ReadingSource.Manual, 70), CheckUp(1, ReadingSource.Wearable, 75)],
            TestWorld.Now.AddDays(-7), TestWorld.Now);

        Assert.Equal([false, true], chart.Points.Select(p => p.FromWatch));
        Assert.True(chart.HasWatchPoints);
        Assert.Contains("Lectura del reloj", TrendChart.TooltipOf(chart, chart.Points[1]));
        Assert.DoesNotContain("Lectura del reloj", TrendChart.TooltipOf(chart, chart.Points[0]));
    }

    [Fact]
    public void A_chart_with_only_typed_readings_has_no_watch_legend()
    {
        ChartMetric metric = Bands.Metrics.First(m => m.Indicator == IndicatorType.HeartRate);

        TrendChartModel chart = TrendChartBuilder.Build(metric, "FC", [CheckUp(1, ReadingSource.Manual, 70)], TestWorld.Now.AddDays(-7), TestWorld.Now);

        Assert.False(chart.HasWatchPoints);
    }

    [Fact]
    public void The_source_saved_in_the_local_database_reaches_the_record_and_the_history_row()
    {
        using TestWorld world = new("ana", online: false);
        Guid ana = TestWorld.IdOf("ana");
        CheckUpRecord saved = world.Store.AddCheckUp(ana, [new ReadingInput(IndicatorType.HeartRate, 70)], world.Evaluator.Evaluate([new ReadingInput(IndicatorType.HeartRate, 70)]));
        using (IServiceScope scope = world.Services.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            foreach (LocalReading reading in db.Set<LocalReading>().Where(r => r.CheckUpId == saved.Id))
            {
                reading.Source = ReadingSource.Wearable;
            }

            db.SaveChanges();
        }

        CheckUpRecord loaded = world.Store.GetCheckUps(ana).First(c => c.Id == saved.Id);
        Assert.True(loaded.Readings[0].FromWatch);

        HistoryViewModel history = new(world.Store, world.Session, world.Info);
        Assert.Contains(history.CheckUps.SelectMany(c => c.Readings), r => r.FromWatch);
    }
}
