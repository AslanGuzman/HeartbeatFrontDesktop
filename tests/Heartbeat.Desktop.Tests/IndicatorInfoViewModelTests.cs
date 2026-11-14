using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

public sealed class IndicatorInfoViewModelTests
{
    private readonly ReferenceRangesInfoProvider _provider = new();
    private readonly FakeUrlLauncher _launcher = new();

    private IndicatorInfoViewModel Create() => new(_provider, _launcher);

    [Fact]
    public void Starts_on_the_first_indicator()
    {
        IndicatorInfoViewModel vm = Create();

        Assert.Equal(_provider.Indicators[0], vm.SelectedIndicator);
        Assert.Equal(6, vm.Indicators.Count);
    }

    [Fact]
    public void Select_moves_to_the_requested_indicator()
    {
        IndicatorInfoViewModel vm = Create();

        vm.Select("sleep");

        Assert.Equal("sleep", vm.SelectedIndicator.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Select_ignores_missing_or_unknown_ids_and_keeps_the_current_one(string? id)
    {
        IndicatorInfoViewModel vm = Create();
        vm.Select("mood");

        vm.Select(id);

        Assert.Equal("mood", vm.SelectedIndicator.Id);
    }

    [Fact]
    public void Opening_a_source_asks_the_launcher_for_that_url_and_shows_no_error()
    {
        IndicatorInfoViewModel vm = Create();
        InfoSource source = vm.SelectedIndicator.Sources[0];

        vm.OpenSourceCommand.Execute(source);

        Assert.Equal([source.Url], _launcher.Opened);
        Assert.False(vm.HasLinkError);
        Assert.Null(vm.LinkErrorText);
    }

    [Fact]
    public void When_the_link_cannot_be_opened_the_address_is_shown_so_it_can_be_copied()
    {
        _launcher.Succeeds = false;
        IndicatorInfoViewModel vm = Create();
        InfoSource source = vm.SelectedIndicator.Sources[0];

        vm.OpenSourceCommand.Execute(source);

        Assert.True(vm.HasLinkError);
        Assert.Contains(source.Url.AbsoluteUri, vm.LinkErrorText);
    }

    [Fact]
    public void The_error_goes_away_when_a_retry_works_or_the_indicator_changes()
    {
        _launcher.Succeeds = false;
        IndicatorInfoViewModel vm = Create();
        InfoSource source = vm.SelectedIndicator.Sources[0];
        vm.OpenSourceCommand.Execute(source);

        _launcher.Succeeds = true;
        vm.OpenSourceCommand.Execute(source);
        Assert.False(vm.HasLinkError);

        _launcher.Succeeds = false;
        vm.OpenSourceCommand.Execute(source);
        Assert.True(vm.HasLinkError);
        vm.Select("mood");
        Assert.False(vm.HasLinkError);
    }

    [Fact]
    public void Opening_a_null_source_does_nothing()
    {
        IndicatorInfoViewModel vm = Create();

        vm.OpenSourceCommand.Execute(null);

        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public void Shows_the_disclaimer_and_the_ranges_version()
    {
        IndicatorInfoViewModel vm = Create();

        Assert.Equal("Herramienta de autoevaluación, no diagnóstico médico", vm.DisclaimerText);
        Assert.Contains(_provider.RangesVersion, vm.RangesVersionText);
    }
}
