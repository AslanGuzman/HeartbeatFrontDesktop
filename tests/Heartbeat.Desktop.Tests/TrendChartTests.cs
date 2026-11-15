using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

/// <summary>Historial y gráficas por indicador con bandas de color y rango de fechas (NR-02, MR-14, #35).</summary>
public sealed class TrendChartTests
{
    private static readonly IndicatorBands Bands = IndicatorBands.Parse(ReferenceRanges.Json);
    private static readonly HealthEvaluator Evaluator = new();

    private static DateTimeOffset Day(int daysAgo) => TestWorld.Now.AddDays(-daysAgo);

    private static CheckUpRecord CheckUp(int daysAgo, params ReadingInput[] inputs)
    {
        ReadingRecord[] readings = inputs.Select(i => new ReadingRecord(i, Evaluator.Evaluate(i))).ToArray();
        return new CheckUpRecord(Guid.NewGuid(), Day(daysAgo), Evaluator.Evaluate(inputs).OverallStatus, readings);
    }

    private static ReadingInput Hr(decimal value) => new(IndicatorType.HeartRate, value);

    // ---------------- Bandas del contrato ----------------

    [Fact]
    public void There_is_one_metric_per_indicator_and_blood_pressure_has_two()
    {
        Assert.Equal(
            ["bloodPressure.value", "bloodPressure.secondaryValue", "heartRate", "bloodSugar", "bodyTemperature", "sleep", "mood"],
            Bands.Metrics.Select(m => m.Key));
        Assert.True(Bands.Metrics[1].IsSecondary);
    }

    [Theory]
    [InlineData("heartRate", 39, HealthStatus.Critical)]
    [InlineData("heartRate", 40, HealthStatus.Watch)]
    [InlineData("heartRate", 50, HealthStatus.Normal)]
    [InlineData("heartRate", 100, HealthStatus.Normal)]
    [InlineData("heartRate", 101, HealthStatus.Watch)]
    [InlineData("heartRate", 121, HealthStatus.Critical)]
    [InlineData("bloodPressure.secondaryValue", 70, HealthStatus.Normal)]
    [InlineData("bloodPressure.secondaryValue", 120, HealthStatus.Critical)]
    [InlineData("sleep", 12, HealthStatus.Watch)]
    public void A_value_takes_the_status_of_the_band_that_contains_it(string key, double value, HealthStatus expected)
    {
        Assert.Equal(expected, Bands.Find(key)!.StatusOf(value));
    }

    [Fact]
    public void The_bands_agree_with_the_evaluator_for_every_integer_heart_rate()
    {
        ChartMetric heart = Bands.Find("heartRate")!;
        for (int bpm = 25; bpm <= 240; bpm++)
        {
            Assert.Equal(Evaluator.Evaluate(Hr(bpm)).Status, heart.StatusOf(bpm));
        }
    }

    // ---------------- Modelo de la gráfica ----------------

    [Fact]
    public void The_chart_shades_every_band_clipped_to_the_axis_and_covers_it_without_gaps()
    {
        ChartMetric heart = Bands.Find("heartRate")!;

        TrendChartModel chart = TrendChartBuilder.Build(heart, "Frecuencia", [CheckUp(2, Hr(70))], Day(7), Day(0));

        Assert.Equal(
            [HealthStatus.Critical, HealthStatus.Watch, HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Critical],
            chart.Bands.Select(b => b.Status));
        Assert.Equal(chart.YMin, chart.Bands[0].From);
        Assert.Equal(chart.YMax, chart.Bands[^1].To);
        for (int i = 1; i < chart.Bands.Count; i++)
        {
            Assert.Equal(chart.Bands[i - 1].To, chart.Bands[i].From);
        }

        Assert.Equal(50, chart.Bands[2].From);
        Assert.Equal(101, chart.Bands[2].To);
    }

    [Fact]
    public void Each_point_carries_the_status_of_its_value_and_points_are_oldest_first()
    {
        ChartMetric heart = Bands.Find("heartRate")!;

        TrendChartModel chart = TrendChartBuilder.Build(
            heart, "Frecuencia", [CheckUp(1, Hr(130)), CheckUp(5, Hr(70)), CheckUp(3, Hr(45))], Day(7), Day(0));

        Assert.Equal([70d, 45d, 130d], chart.Points.Select(p => p.Value));
        Assert.Equal([HealthStatus.Normal, HealthStatus.Watch, HealthStatus.Critical], chart.Points.Select(p => p.Status));
    }

    [Fact]
    public void The_axis_makes_room_for_values_outside_the_bands()
    {
        ChartMetric heart = Bands.Find("heartRate")!;

        TrendChartModel chart = TrendChartBuilder.Build(heart, "Frecuencia", [CheckUp(1, Hr(200)), CheckUp(2, Hr(30))], Day(7), Day(0));

        Assert.True(chart.YMin < 30);
        Assert.True(chart.YMax > 200);
        Assert.True(chart.YMin >= 20 && chart.YMax <= 250);
    }

    [Fact]
    public void The_axis_never_leaves_the_valid_range()
    {
        ChartMetric mood = Bands.Find("mood")!;

        TrendChartModel chart = TrendChartBuilder.Build(mood, "Ánimo", [CheckUp(1, new ReadingInput(IndicatorType.Mood, 5))], Day(7), Day(0));

        Assert.True(chart.YMin >= 1);
        Assert.True(chart.YMax <= 5);
    }

    [Fact]
    public void Ticks_are_round_numbers_inside_the_axis()
    {
        IReadOnlyList<double> ticks = TrendChartBuilder.NiceTicks(37, 126, 5);

        Assert.Equal([40d, 60, 80, 100, 120], ticks);
        Assert.Equal([1d, 2, 3, 4, 5], TrendChartBuilder.NiceTicks(1, 5, 5));
        Assert.Equal([35d, 36, 37, 38, 39], TrendChartBuilder.NiceTicks(34.6, 39.4, 5).Where(t => t == Math.Round(t)));
    }

    [Fact]
    public void Only_readings_inside_the_period_are_drawn()
    {
        ChartMetric heart = Bands.Find("heartRate")!;

        TrendChartModel chart = TrendChartBuilder.Build(
            heart, "Frecuencia", [CheckUp(40, Hr(60)), CheckUp(10, Hr(70)), CheckUp(1, Hr(80))], Day(30), Day(0));

        Assert.Equal([70d, 80d], chart.Points.Select(p => p.Value));
        Assert.Equal(Day(30), chart.XMin);
        Assert.Equal(Day(0), chart.XMax);
    }

    [Fact]
    public void A_check_up_without_the_indicator_adds_no_point_and_an_empty_chart_keeps_its_bands()
    {
        ChartMetric heart = Bands.Find("heartRate")!;

        TrendChartModel chart = TrendChartBuilder.Build(
            heart, "Frecuencia", [CheckUp(1, new ReadingInput(IndicatorType.Sleep, 7))], Day(7), Day(0));

        Assert.False(chart.HasData);
        Assert.NotEmpty(chart.Bands);
    }

    [Fact]
    public void Blood_pressure_draws_systolic_and_diastolic_against_their_own_bands()
    {
        ReadingInput bp = new(IndicatorType.BloodPressure, 185, 75);
        CheckUpRecord[] checkUps = [CheckUp(1, bp)];

        TrendChartModel systolic = TrendChartBuilder.Build(Bands.Find("bloodPressure.value")!, "Sistólica", checkUps, Day(7), Day(0));
        TrendChartModel diastolic = TrendChartBuilder.Build(Bands.Find("bloodPressure.secondaryValue")!, "Diastólica", checkUps, Day(7), Day(0));

        Assert.Equal((185d, HealthStatus.Critical), (systolic.Points[0].Value, systolic.Points[0].Status));
        Assert.Equal((75d, HealthStatus.Normal), (diastolic.Points[0].Value, diastolic.Points[0].Status));
    }

    [Fact]
    public void The_tooltip_names_the_status_not_just_the_color()
    {
        ChartMetric heart = Bands.Find("heartRate")!;
        TrendChartModel chart = TrendChartBuilder.Build(heart, "Frecuencia", [CheckUp(1, Hr(130))], Day(7), Day(0));

        string text = TrendChart.TooltipOf(chart, chart.Points[0]);

        Assert.Contains("130", text);
        Assert.Contains("lpm", text);
        Assert.Contains("Crítico", text);
    }

    // ---------------- Pantalla ----------------

    private static HistoryViewModel NewVm(TestWorld world, string? metric = null)
    {
        HistoryViewModel vm = new(world.Store, world.Session, world.Info, Bands, world.Clock);
        if (metric is not null)
        {
            vm.SelectMetricCommand.Execute(vm.Metrics.First(m => m.Metric.Key == metric));
        }

        return vm;
    }

    private static void AddCheckUp(TestWorld world, int daysAgo, decimal heartRate)
    {
        DateTimeOffset previous = world.Clock.Now;
        world.Clock.Now = Day(daysAgo);
        ReadingInput[] inputs = [Hr(heartRate)];
        world.Store.AddCheckUp(TestWorld.IdOf("ana"), inputs, Evaluator.Evaluate(inputs));
        world.Clock.Now = previous;
    }

    [Fact]
    public void The_history_starts_on_the_whole_mission_with_heart_rate_selected()
    {
        TestWorld world = new();

        HistoryViewModel vm = NewVm(world);

        Assert.Equal(HistoryRange.Mission, vm.Range);
        Assert.True(vm.IsMission);
        Assert.Equal("heartRate", vm.SelectedMetric!.Metric.Key);
        Assert.Equal(["heartRate"], vm.Metrics.Where(m => m.IsSelected).Select(m => m.Metric.Key));
        Assert.Equal(7, vm.Metrics.Count);
        Assert.Contains("toda la misión", vm.ChartSummary);
    }

    [Fact]
    public void The_period_filters_both_the_chart_and_the_list()
    {
        TestWorld world = new("mara");
        world.Clock.Now = TestWorld.Now;
        AddMara(world, 40, 60);
        AddMara(world, 20, 70);
        AddMara(world, 3, 80);
        HistoryViewModel vm = new(world.Store, world.Session, world.Info, Bands, world.Clock);
        int all = vm.CheckUps.Count;

        vm.SelectRangeCommand.Execute(HistoryRange.Month);
        int month = vm.CheckUps.Count;
        Assert.Equal(["Frecuencia cardíaca"], new[] { vm.SelectedMetric!.Name });
        Assert.All(vm.Chart.Points, p => Assert.True(p.At >= Day(30)));

        vm.SelectRangeCommand.Execute(HistoryRange.Week);
        int week = vm.CheckUps.Count;

        Assert.True(all > month && month > week);
        Assert.True(week >= 1);
        Assert.Contains("últimos 7 días", vm.ChartSummary);
        Assert.True(vm.IsWeek);
        Assert.False(vm.IsMission);
    }

    private static void AddMara(TestWorld world, int daysAgo, decimal heartRate)
    {
        DateTimeOffset previous = world.Clock.Now;
        world.Clock.Now = Day(daysAgo);
        ReadingInput[] inputs = [Hr(heartRate)];
        world.Store.AddCheckUp(TestWorld.IdOf("mara"), inputs, Evaluator.Evaluate(inputs));
        world.Clock.Now = previous;
    }

    [Fact]
    public void Choosing_an_indicator_redraws_the_chart_for_it()
    {
        TestWorld world = new();
        HistoryViewModel vm = NewVm(world);
        TrendChartModel heart = vm.Chart;

        vm.SelectMetricCommand.Execute(vm.Metrics.First(m => m.Metric.Key == "sleep"));

        Assert.NotSame(heart, vm.Chart);
        Assert.Equal("h", vm.Chart.Unit.Replace("hours/day", "h"));
        Assert.Equal(["sleep"], vm.Metrics.Where(m => m.IsSelected).Select(m => m.Metric.Key));
        Assert.StartsWith("Sueño", vm.ChartSummary);
    }

    [Fact]
    public void The_chart_follows_new_check_ups()
    {
        TestWorld world = new("ana");
        HistoryViewModel vm = NewVm(world);
        int before = vm.Chart.Points.Count;

        AddCheckUp(world, 0, 77);

        Assert.Equal(before + 1, vm.Chart.Points.Count);
    }

    [Fact]
    public void An_empty_period_says_so_but_no_check_ups_at_all_says_something_else()
    {
        TestWorld empty = new(signedInUser: null);
        HistoryViewModel none = new(empty.Store, empty.Session, empty.Info, Bands, empty.Clock);
        Assert.False(none.HasItems);
        Assert.StartsWith("Aún no hay check-ups", none.EmptyText);
        Assert.False(none.Chart.HasData);

        // Un año después de los check-ups sembrados: hay historial, pero nada en los últimos 7 días.
        TestWorld world = new("mara");
        world.Clock.Now = TestWorld.Now.AddDays(400);
        HistoryViewModel vm = new(world.Store, world.Session, world.Info, Bands, world.Clock);
        vm.SelectRangeCommand.Execute(HistoryRange.Week);

        Assert.StartsWith("No hay check-ups en este período", vm.EmptyText);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public void Blood_pressure_options_say_which_value_they_draw()
    {
        TestWorld world = new();

        HistoryViewModel vm = NewVm(world);

        Assert.Contains(vm.Metrics, m => m.Name.EndsWith("· sistólica", StringComparison.Ordinal));
        Assert.Contains(vm.Metrics, m => m.Name.EndsWith("· diastólica", StringComparison.Ordinal));
    }
}
