using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

public sealed class BodyMeasurementsViewModelTests
{
    private readonly TestWorld _world;
    private readonly DemoBodyMeasurementStore _store;
    private readonly BodyMeasurementsViewModel _body;

    public BodyMeasurementsViewModelTests()
    {
        _world = new TestWorld("ana");
        _store = new DemoBodyMeasurementStore(_world.Clock);
        _body = new BodyMeasurementsViewModel(_store, _world.Session);
    }

    private BodyMetricViewModel Metric(BodyRegion region) => _body.Metrics.Single(m => m.Region == region);

    [Fact]
    public void Shows_the_latest_value_and_the_change_against_the_previous_one()
    {
        // Datos de ejemplo de ana: altura 168,0 -> 168,6 y pantorrilla 36,0 -> 35,5.
        Assert.Contains("168", Metric(BodyRegion.Height).CurrentText);
        Assert.StartsWith("▲", Metric(BodyRegion.Height).DeltaText);
        Assert.StartsWith("▼", Metric(BodyRegion.Calf).DeltaText);
    }

    [Fact]
    public void Starts_with_the_six_regions_and_height_selected()
    {
        Assert.Equal(6, _body.Metrics.Count);
        Assert.True(_body.IsHeightSelected);
        Assert.Equal([Metric(BodyRegion.Height)], _body.Metrics.Where(m => m.IsSelected));
    }

    [Fact]
    public void Selecting_a_region_highlights_only_that_one()
    {
        _body.Select(Metric(BodyRegion.Waist));

        Assert.False(_body.IsHeightSelected);
        Assert.Equal([Metric(BodyRegion.Waist)], _body.Metrics.Where(m => m.IsSelected));
    }

    [Fact]
    public void Saving_stores_only_the_filled_fields_clears_them_and_updates_the_change()
    {
        Metric(BodyRegion.Waist).Input = "68,5";

        _body.SaveCommand.Execute(null);

        Assert.Null(_body.Error);
        Assert.Equal("Medida guardada.", _body.SavedText);
        Assert.Equal(string.Empty, Metric(BodyRegion.Waist).Input);
        Assert.StartsWith("▼", Metric(BodyRegion.Waist).DeltaText);
        Assert.Equal(3, _store.GetHistory("ana", BodyRegion.Waist).Count);
        Assert.Equal(2, _store.GetHistory("ana", BodyRegion.Height).Count);
    }

    [Fact]
    public void Saving_without_values_asks_for_at_least_one()
    {
        _body.SaveCommand.Execute(null);

        Assert.Equal("Escribe al menos una medida.", _body.Error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1 2")]
    public void A_non_numeric_value_is_rejected_naming_the_region(string text)
    {
        Metric(BodyRegion.Chest).Input = text;

        _body.SaveCommand.Execute(null);

        Assert.Contains("Pecho", _body.Error);
        Assert.Equal(2, _store.GetHistory("ana", BodyRegion.Chest).Count);
    }

    [Theory]
    [InlineData(BodyRegion.Height, "20")]
    [InlineData(BodyRegion.Neck, "300")]
    public void A_value_outside_what_can_be_measured_is_rejected_and_nothing_is_saved(BodyRegion region, string text)
    {
        int before = _store.GetHistory("ana", region).Count;
        Metric(region).Input = text;

        _body.SaveCommand.Execute(null);

        Assert.Contains(Metric(region).Name, _body.Error);
        Assert.Equal(before, _store.GetHistory("ana", region).Count);
    }

    [Fact]
    public void Without_a_session_nothing_is_saved()
    {
        TestWorld world = new(signedInUser: null);
        BodyMeasurementsViewModel body = new(new DemoBodyMeasurementStore(world.Clock), world.Session);
        body.Metrics[0].Input = "170";

        body.SaveCommand.Execute(null);

        Assert.NotNull(body.Error);
        Assert.Equal("—", body.Metrics[0].CurrentText);
    }
}
