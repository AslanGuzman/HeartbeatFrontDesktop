using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class DashboardViewModelTests
{
    private static DashboardViewModel Create(TestWorld world) =>
        new(world.Store, world.Session, world.Info, world.Presenter, world.Clock);

    [Fact]
    public void Shows_the_six_indicators_with_the_latest_reading_of_the_signed_in_astronaut()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal(
            ["bloodPressure", "heartRate", "bloodSugar", "bodyTemperature", "sleep", "mood"],
            dashboard.Cards.Select(c => c.Id));
        Assert.All(dashboard.Cards, card => Assert.True(card.HasData));
        Assert.Equal("120/78 mmHg", dashboard.Cards[0].ValueText);
        Assert.Equal("70 lpm", dashboard.Cards[1].ValueText);
    }

    [Fact]
    public void Greets_by_first_name_and_shows_the_overall_state_as_text()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal("Hola, Ana", dashboard.Greeting);
        Assert.Equal(HealthStatus.Normal, dashboard.OverallStatus);
        Assert.Equal("Estado general: Normal", dashboard.OverallText);
    }

    [Fact]
    public void Every_card_carries_the_status_name_so_color_is_never_the_only_signal()
    {
        DashboardViewModel dashboard = Create(new TestWorld("luis"));

        Assert.All(dashboard.Cards, card => Assert.Contains(card.StatusText, new[] { "Normal", "Vigilancia", "Crítico" }));
        // luis tiene 52 mg/dL de glucosa en su último check-up: crítico.
        Assert.Equal(HealthStatus.Critical, dashboard.OverallStatus);
        Assert.Equal("Crítico", dashboard.Cards.Single(c => c.Id == "bloodSugar").StatusText);
    }

    [Fact]
    public void Without_a_session_there_is_no_data_and_each_card_says_so()
    {
        DashboardViewModel dashboard = Create(new TestWorld(signedInUser: null));

        Assert.False(dashboard.HasData);
        Assert.Null(dashboard.OverallStatus);
        Assert.Equal("Sin mediciones todavía", dashboard.OverallText);
        Assert.All(dashboard.Cards, card =>
        {
            Assert.False(card.HasData);
            Assert.Null(card.Status);
            Assert.Equal("—", card.ValueText);
        });
    }

    [Fact]
    public void Cards_use_the_spanish_indicator_names()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal(
            ["Presión arterial", "Frecuencia cardíaca", "Glucosa", "Temperatura corporal", "Sueño", "Estado de ánimo"],
            dashboard.Cards.Select(c => c.Name));
    }

    [Fact]
    public void Show_info_for_an_indicator_opens_the_panel_on_it()
    {
        TestWorld world = new();
        DashboardViewModel dashboard = Create(world);

        dashboard.ShowInfoCommand.Execute("heartRate");

        Assert.Equal(["heartRate"], world.Presenter.Shown);
    }

    [Fact]
    public void Show_info_without_an_indicator_opens_the_panel_as_is()
    {
        TestWorld world = new();
        DashboardViewModel dashboard = Create(world);

        dashboard.ShowInfoCommand.Execute(null);

        Assert.Equal([(string?)null], world.Presenter.Shown);
    }
}
