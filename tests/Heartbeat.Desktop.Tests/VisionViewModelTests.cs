using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>HB-FE-04: registro de IOP y agudeza, tendencia con gráfica y nota SANS con fuente.</summary>
public sealed class VisionViewModelTests
{
    private static VisionViewModel Create(TestWorld world, FakeUrlLauncher? launcher = null) =>
        new(new LocalVisionStore(world.Services.GetRequiredService<IServiceScopeFactory>(), world.Clock), world.Session, launcher ?? new FakeUrlLauncher(), world.Clock);

    private static TestWorld Fresh()
    {
        TestWorld world = new(signedInUser: null);
        world.Session.SignIn(new SessionUser(Guid.NewGuid(), "nueva", "Nueva Persona"));
        return world;
    }

    // ---- Datos de demostración y tendencia ----

    [Fact]
    public void Shows_iop_and_visual_acuity_with_their_status_and_a_chart_with_points()
    {
        VisionViewModel vision = Create(new TestWorld("ana"));

        Assert.Equal(["iop", "visualAcuity"], vision.Indicators.Select(i => i.Id));
        Assert.All(vision.Indicators, i =>
        {
            Assert.True(i.HasData);
            Assert.Contains(i.StatusText, new[] { "Normal", "Vigilancia", "Crítico" });
            Assert.True(i.Chart.HasData);
            Assert.NotEmpty(i.Chart.Bands);
        });
        Assert.Equal("mmHg", vision.Indicators[0].UnitText);
        Assert.Equal("logMAR", vision.Indicators[1].UnitText);
    }

    [Fact]
    public void A_rising_iop_is_reported_as_rising_and_shows_a_correlation_with_co2()
    {
        VisionViewModel vision = Create(new TestWorld("luis"));

        VisionIndicatorViewModel iop = vision.Indicators[0];
        Assert.Contains("subiendo", iop.TrendText);
        Assert.Equal(HealthStatus.Watch, iop.Status);
        Assert.Contains("Relación con el CO₂", iop.CorrelationText);
        Assert.Contains("fuerte", iop.CorrelationText);
    }

    [Fact]
    public void A_stable_iop_is_reported_as_stable()
    {
        VisionViewModel vision = Create(new TestWorld("ana"));

        Assert.Contains("estable", vision.Indicators[0].TrendText);
        Assert.Equal(HealthStatus.Normal, vision.Indicators[0].Status);
    }

    [Fact]
    public void The_chart_bands_come_from_the_catalog()
    {
        VisionViewModel vision = Create(new TestWorld("ana"));

        IndicatorCatalogEntry iop = IndicatorsCatalog.Find("iop")!;
        Assert.Equal(
            iop.Bands.Select(b => b.Status).Order(),
            vision.Indicators[0].Chart.Bands.Select(b => b.Status).Order());
    }

    // ---- Registro ----

    [Fact]
    public void A_new_astronaut_sees_no_data_and_asks_for_readings()
    {
        VisionViewModel vision = Create(Fresh());

        Assert.All(vision.Indicators, i =>
        {
            Assert.False(i.HasData);
            Assert.Null(i.Status);
            Assert.Equal("Sin datos", i.StatusText);
        });
        Assert.Null(vision.OverallStatus);
        Assert.Equal("Aún no hay lecturas", vision.OverallText);
    }

    [Fact]
    public void Saving_both_values_adds_a_point_to_each_chart_and_clears_the_form()
    {
        TestWorld world = Fresh();
        VisionViewModel vision = Create(world);
        vision.IopText = "17,5";
        vision.AcuityText = "0.1";

        vision.AddCommand.Execute(null);

        Assert.Equal(string.Empty, vision.Error);
        Assert.Equal(string.Empty, vision.IopText);
        Assert.Equal(string.Empty, vision.AcuityText);
        Assert.Equal("Lectura guardada en este equipo.", vision.Confirmation);
        Assert.Single(vision.Indicators[0].Chart.Points);
        Assert.Single(vision.Indicators[1].Chart.Points);
        Assert.Equal("17,5", vision.Indicators[0].ValueText);
        Assert.Equal(HealthStatus.Normal, vision.OverallStatus);
    }

    [Fact]
    public void Only_one_value_is_enough()
    {
        VisionViewModel vision = Create(Fresh());
        vision.IopText = "26";

        vision.AddCommand.Execute(null);

        Assert.Single(vision.Indicators[0].Chart.Points);
        Assert.Empty(vision.Indicators[1].Chart.Points);
        Assert.Equal(HealthStatus.Critical, vision.Indicators[0].Status);
        Assert.Equal(HealthStatus.Critical, vision.OverallStatus);
    }

    [Theory]
    [InlineData("", "", "Escribe al menos un valor.")]
    [InlineData("abc", "", "La presión ocular debe ser un número mayor que 0 y de hasta 80 mmHg.")]
    [InlineData("0", "", "La presión ocular debe ser un número mayor que 0 y de hasta 80 mmHg.")]
    [InlineData("81", "", "La presión ocular debe ser un número mayor que 0 y de hasta 80 mmHg.")]
    [InlineData("", "x", "La agudeza visual debe ser un número entre -0,5 y 3,0 logMAR.")]
    [InlineData("", "3.5", "La agudeza visual debe ser un número entre -0,5 y 3,0 logMAR.")]
    public void Invalid_input_shows_an_error_and_saves_nothing(string iop, string acuity, string expected)
    {
        VisionViewModel vision = Create(Fresh());
        vision.IopText = iop;
        vision.AcuityText = acuity;

        vision.AddCommand.Execute(null);

        Assert.Equal(expected, vision.Error);
        Assert.All(vision.Indicators, i => Assert.False(i.HasData));
    }

    [Fact]
    public void An_invalid_value_does_not_save_the_valid_one_either()
    {
        VisionViewModel vision = Create(Fresh());
        vision.IopText = "16";
        vision.AcuityText = "oops";

        vision.AddCommand.Execute(null);

        Assert.NotEmpty(vision.Error);
        Assert.All(vision.Indicators, i => Assert.False(i.HasData));
    }

    [Fact]
    public void Readings_are_kept_per_astronaut()
    {
        TestWorld world = new("ana");
        VisionViewModel vision = Create(world);
        int before = vision.Indicators[0].Chart.Points.Count;
        vision.IopText = "16";
        vision.AddCommand.Execute(null);
        Assert.Equal(before + 1, vision.Indicators[0].Chart.Points.Count);

        world.Session.SignIn(Heartbeat.Desktop.Demo.DemoAccounts.All.First(a => a.UserName == "luis"));

        Assert.Equal(6, Create(world).Indicators[0].Chart.Points.Count);
    }

    // ---- Nota SANS, fuente y etiqueta ----

    [Fact]
    public void The_sans_note_links_sans_with_co2_and_cites_the_catalog_source()
    {
        TestWorld world = new("ana");
        FakeUrlLauncher launcher = new();
        VisionViewModel vision = Create(world, launcher);

        Assert.Contains("SANS", vision.SansNote);
        Assert.Contains("CO₂", vision.SansNote);
        Assert.Contains("simulados", vision.SansNote);
        Assert.Contains("NASA", vision.SourceTitle);
        Assert.Contains("heurístico", vision.EvidenceText);

        vision.OpenSourceCommand.Execute(null);

        Assert.Equal(IndicatorsCatalog.SourceFor("nasa-sans").Url, launcher.Opened.Single());
    }

    [Fact]
    public void A_failed_link_shows_an_error()
    {
        VisionViewModel vision = Create(new TestWorld("ana"), new FakeUrlLauncher(succeeds: false));

        vision.OpenSourceCommand.Execute(null);

        Assert.NotEmpty(vision.Error);
    }

    [Fact]
    public void The_page_is_titled_vision()
    {
        Assert.Equal("Visión", Create(new TestWorld("ana")).Title);
    }

    [Fact]
    public void The_dashboard_gravity_light_uses_the_saved_vision_readings()
    {
        TestWorld world = Fresh();
        VisionViewModel vision = Create(world);
        vision.IopText = "27";
        vision.AddCommand.Execute(null);

        DashboardViewModel dashboard = new(world.Store, world.Session, world.Info, world.Presenter, world.Navigation, world.Clock, world.Reminder, world.ProfileStore, world.EnvironmentStore);

        Assert.Equal(HealthStatus.Critical, dashboard.Simple.Hazards.Single(h => h.Hazard == Hazard.GravityFields).Status);
    }
}
