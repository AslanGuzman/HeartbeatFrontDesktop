using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

/// <summary>Figura humana con el estado por región; clic abre la información del indicador (NR-04, #48).</summary>
public sealed class BodyMapTests
{
    private static DashboardViewModel Create(TestWorld world) =>
        new(world.Store, world.Session, world.Info, world.Presenter, world.Navigation, world.Clock, world.Reminder);

    private static void Add(TestWorld world, string user, params ReadingInput[] inputs) =>
        world.Store.AddCheckUp(TestWorld.IdOf(user), inputs, world.Evaluator.Evaluate(inputs));

    [Fact]
    public void The_regions_come_from_the_body_region_of_the_contract()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal(["head", "heart", "abdomen"], dashboard.Regions.Select(r => r.Id));
        Assert.Equal(
            ["bodyTemperature", "sleep", "mood"],
            dashboard.Regions.Single(r => r.Id == "head").Indicators.Select(i => i.Id));
        Assert.Equal(
            ["bloodPressure", "heartRate"],
            dashboard.Regions.Single(r => r.Id == "heart").Indicators.Select(i => i.Id));
        Assert.Equal(["bloodSugar"], dashboard.Regions.Single(r => r.Id == "abdomen").Indicators.Select(i => i.Id));
    }

    [Fact]
    public void Every_indicator_belongs_to_exactly_one_region()
    {
        TestWorld world = new("ana");
        DashboardViewModel dashboard = Create(world);

        string[] shown = dashboard.Regions.SelectMany(r => r.Indicators).Select(i => i.Id).ToArray();

        Assert.Equal(world.Info.Indicators.Select(i => i.Id).Order(), shown.Order());
        Assert.All(world.Info.Indicators, i => Assert.Contains(i.BodyRegion, new[] { "head", "heart", "abdomen" }));
    }

    [Fact]
    public void A_region_takes_the_worst_status_of_its_indicators()
    {
        TestWorld world = new("mara");
        Add(world, "mara",
            new ReadingInput(IndicatorType.HeartRate, 130),
            new ReadingInput(IndicatorType.BloodPressure, 150, 95),
            new ReadingInput(IndicatorType.BodyTemperature, 36.8m),
            new ReadingInput(IndicatorType.Sleep, 5),
            new ReadingInput(IndicatorType.Mood, 4),
            new ReadingInput(IndicatorType.BloodSugar, 90));

        DashboardViewModel dashboard = Create(world);

        Assert.Equal(HealthStatus.Critical, dashboard.Regions.Single(r => r.Id == "heart").Status);
        Assert.Equal(HealthStatus.Watch, dashboard.Regions.Single(r => r.Id == "head").Status);
        Assert.Equal(HealthStatus.Normal, dashboard.Regions.Single(r => r.Id == "abdomen").Status);
        Assert.Equal("Crítico", dashboard.Regions.Single(r => r.Id == "heart").StatusText);
    }

    [Fact]
    public void A_region_without_readings_has_no_status_and_says_so()
    {
        TestWorld world = new("ana");
        // Un check-up nuevo solo con la glucosa deja la cabeza y el corazón con sus últimas lecturas, así que se usa un usuario sin datos.
        TestWorld none = new(signedInUser: null);
        DashboardViewModel dashboard = Create(none);

        Assert.All(dashboard.Regions, r => Assert.Null(r.Status));
        Assert.All(dashboard.Regions, r => Assert.Equal("Sin datos", r.StatusText));
        Assert.All(dashboard.Regions, r => Assert.EndsWith("sin mediciones", r.SummaryText));
        Assert.NotNull(world);
    }

    [Fact]
    public void Clicking_a_region_opens_the_info_of_its_most_serious_indicator()
    {
        TestWorld world = new("mara");
        Add(world, "mara",
            new ReadingInput(IndicatorType.HeartRate, 130),
            new ReadingInput(IndicatorType.BloodPressure, 150, 95));
        DashboardViewModel dashboard = Create(world);

        dashboard.ShowRegionCommand.Execute("heart");

        Assert.Equal(["heartRate"], world.Presenter.Shown);
    }

    [Fact]
    public void On_a_tie_the_first_indicator_of_the_region_wins_and_with_no_data_it_still_opens_one()
    {
        TestWorld world = new("mara");
        Add(world, "mara",
            new ReadingInput(IndicatorType.HeartRate, 70),
            new ReadingInput(IndicatorType.BloodPressure, 120, 78));
        DashboardViewModel dashboard = Create(world);

        dashboard.ShowRegionCommand.Execute("heart");

        Assert.Equal(["bloodPressure"], world.Presenter.Shown);

        TestWorld none = new(signedInUser: null);
        Create(none).ShowRegionCommand.Execute("abdomen");
        Assert.Equal(["bloodSugar"], none.Presenter.Shown);
    }

    [Fact]
    public void An_unknown_region_opens_nothing()
    {
        TestWorld world = new("ana");
        DashboardViewModel dashboard = Create(world);

        dashboard.ShowRegionCommand.Execute("knee");
        dashboard.ShowRegionCommand.Execute(null);

        Assert.Empty(world.Presenter.Shown);
    }

    [Fact]
    public void Each_region_lists_its_indicators_with_value_and_status_as_text()
    {
        TestWorld world = new("mara");
        Add(world, "mara", new ReadingInput(IndicatorType.HeartRate, 130));
        DashboardViewModel dashboard = Create(world);

        RegionIndicatorViewModel heart = dashboard.Regions.Single(r => r.Id == "heart").Indicators.Single(i => i.Id == "heartRate");

        Assert.Equal("130 lpm", heart.ValueText);
        Assert.Equal("Crítico", heart.StatusText);
        Assert.Equal(HealthStatus.Critical, heart.Status);
    }

    [Fact]
    public void Region_shapes_are_valid_path_data()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        foreach (BodyRegionViewModel region in dashboard.Regions)
        {
            System.Windows.Media.Geometry geometry = System.Windows.Media.Geometry.Parse(region.Geometry);
            Assert.False(geometry.Bounds.IsEmpty);
        }
    }
}
