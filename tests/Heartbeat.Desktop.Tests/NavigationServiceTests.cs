using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class NavigationServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly NavigationService _navigation;

    public NavigationServiceTests()
    {
        ServiceCollection services = new();
        services.AddTransient<DisposablePageViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<FailingPageViewModel>();
        _provider = services.BuildServiceProvider();
        _navigation = new NavigationService(_provider);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Starts_without_a_page()
    {
        Assert.Null(_navigation.CurrentPage);
    }

    [Fact]
    public void NavigateTo_sets_the_current_page_and_raises_the_event()
    {
        int raised = 0;
        _navigation.CurrentPageChanged += (_, _) => raised++;

        HistoryViewModel page = _navigation.NavigateTo<HistoryViewModel>();

        Assert.Same(page, _navigation.CurrentPage);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void NavigateTo_runs_the_initializer_before_the_page_is_published()
    {
        string? seenByListener = null;
        _navigation.CurrentPageChanged += (_, _) => seenByListener = ((DisposablePageViewModel)_navigation.CurrentPage!).Parameter;

        _navigation.NavigateTo<DisposablePageViewModel>(page => page.Parameter = "checkup-42");

        Assert.Equal("checkup-42", seenByListener);
    }

    [Fact]
    public void NavigateTo_disposes_the_previous_page()
    {
        DisposablePageViewModel first = _navigation.NavigateTo<DisposablePageViewModel>();

        _navigation.NavigateTo<HistoryViewModel>();

        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void NavigateTo_the_same_page_without_initializer_keeps_the_instance()
    {
        DisposablePageViewModel first = _navigation.NavigateTo<DisposablePageViewModel>();
        int raised = 0;
        _navigation.CurrentPageChanged += (_, _) => raised++;

        DisposablePageViewModel second = _navigation.NavigateTo<DisposablePageViewModel>();

        Assert.Same(first, second);
        Assert.False(first.IsDisposed);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void NavigateTo_the_same_page_with_initializer_creates_a_new_instance()
    {
        DisposablePageViewModel first = _navigation.NavigateTo<DisposablePageViewModel>();

        DisposablePageViewModel second = _navigation.NavigateTo<DisposablePageViewModel>(page => page.Parameter = "x");

        Assert.NotSame(first, second);
        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void NavigateTo_keeps_the_current_page_when_the_new_one_cannot_be_created()
    {
        DisposablePageViewModel current = _navigation.NavigateTo<DisposablePageViewModel>();

        Assert.Throws<InvalidOperationException>(() => _navigation.NavigateTo<FailingPageViewModel>());

        Assert.Same(current, _navigation.CurrentPage);
        Assert.False(current.IsDisposed);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(PageViewModel))]
    public void NavigateTo_rejects_types_that_are_not_concrete_pages(Type type)
    {
        Assert.Throws<ArgumentException>(() => _navigation.NavigateTo(type));
    }
}
