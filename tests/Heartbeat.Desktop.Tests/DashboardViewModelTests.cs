using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

public sealed class DashboardViewModelTests
{
    private readonly FakeIndicatorInfoPresenter _presenter = new();

    private DashboardViewModel Create() => new(new ReferenceRangesInfoProvider(), _presenter);

    [Fact]
    public void Lists_the_six_indicators_that_have_information()
    {
        Assert.Equal(6, Create().Indicators.Count);
    }

    [Fact]
    public void Show_info_for_an_indicator_opens_the_panel_on_it()
    {
        DashboardViewModel dashboard = Create();

        dashboard.ShowInfoCommand.Execute("heartRate");

        Assert.Equal(["heartRate"], _presenter.Shown);
    }

    [Fact]
    public void Show_info_without_an_indicator_opens_the_panel_as_is()
    {
        DashboardViewModel dashboard = Create();

        dashboard.ShowInfoCommand.Execute(null);

        Assert.Equal([(string?)null], _presenter.Shown);
    }
}
