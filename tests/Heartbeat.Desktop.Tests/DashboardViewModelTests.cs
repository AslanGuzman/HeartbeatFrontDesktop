using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class DashboardViewModelTests
{
    private static DashboardViewModel Create(TestWorld world) =>
        new(world.Store, world.Session, world.Info, world.Presenter, world.Navigation, world.Clock, world.Reminder);

    private static void Add(TestWorld world, string user, params ReadingInput[] inputs) =>
        world.Store.AddCheckUp(TestWorld.IdOf(user), inputs, world.Evaluator.Evaluate(inputs));

    // ---- Tarjeta por indicador: último valor, unidad, estado con color y hora ----

    [Fact]
    public void Shows_one_card_for_each_of_the_six_indicators_in_order()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal(
            ["bloodPressure", "heartRate", "bloodSugar", "bodyTemperature", "sleep", "mood"],
            dashboard.Cards.Select(c => c.Id));
        Assert.All(dashboard.Cards, card => Assert.True(card.HasData));
    }

    [Fact]
    public void Each_card_has_the_latest_value_and_its_unit_apart()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        (string Value, string Unit)[] expected =
        [
            ("120/78", "mmHg"), ("70", "lpm"), ("90", "mg/dL"), (36.7m.ToString("0.0"), "°C"), ("7", "h"), ("4", "/ 5"),
        ];
        Assert.Equal(expected, dashboard.Cards.Select(c => (c.ValueText, c.UnitText)));
    }

    [Fact]
    public void Each_card_carries_the_status_name_so_color_is_never_the_only_signal()
    {
        DashboardViewModel dashboard = Create(new TestWorld("luis"));

        Assert.All(dashboard.Cards, card => Assert.Contains(card.StatusText, new[] { "Normal", "Vigilancia", "Crítico" }));
        Assert.Equal("Crítico", dashboard.Cards.Single(c => c.Id == "bloodSugar").StatusText);
        Assert.Equal(HealthStatus.Critical, dashboard.Cards.Single(c => c.Id == "bloodSugar").Status);
        Assert.Equal("Vigilancia", dashboard.Cards.Single(c => c.Id == "bloodPressure").StatusText);
        Assert.Equal("Normal", dashboard.Cards.Single(c => c.Id == "heartRate").StatusText);
    }

    [Fact]
    public void Each_card_says_when_it_was_measured_in_plain_words()
    {
        TestWorld world = new("ana");

        DashboardViewModel dashboard = Create(world);

        Assert.All(dashboard.Cards, card => Assert.StartsWith("Ayer, ", card.TakenAtText));
    }

    [Theory]
    [InlineData(1, "Hoy, ")]
    [InlineData(25, "Ayer, ")]
    [InlineData(24 * 3, "Hace 3 días, ")]
    [InlineData(24 * 6, "Hace 6 días, ")]
    public void The_time_reads_today_yesterday_or_days_ago(int hoursAgo, string expectedStart)
    {
        TestWorld world = Fresh();
        MeasuredAt(world, TimeSpan.FromHours(hoursAgo));

        Assert.StartsWith(expectedStart, Create(world).Cards.Single(c => c.Id == "heartRate").TakenAtText);
    }

    [Fact]
    public void An_old_reading_shows_the_full_date()
    {
        TestWorld world = Fresh();
        MeasuredAt(world, TimeSpan.FromDays(20));

        string text = Create(world).Cards.Single(c => c.Id == "heartRate").TakenAtText;

        Assert.DoesNotContain("Hace", text);
        Assert.DoesNotContain("Hoy", text);
        Assert.DoesNotContain("Ayer", text);
    }

    // Un astronauta nuevo (sin historial sembrado) con una lectura de pulso hecha hace el tiempo indicado.
    private static TestWorld Fresh()
    {
        TestWorld world = new(signedInUser: null);
        world.Session.SignIn(new SessionUser(Guid.NewGuid(), "nueva", "Nueva Persona"));
        return world;
    }

    private static void MeasuredAt(TestWorld world, TimeSpan ago)
    {
        DateTimeOffset now = world.Clock.Now;
        world.Clock.Now = now - ago;
        ReadingInput input = new(IndicatorType.HeartRate, 70);
        world.Store.AddCheckUp(world.Session.CurrentUser!.Id, [input], world.Evaluator.Evaluate([input]));
        world.Clock.Now = now;
    }

    [Fact]
    public void An_indicator_never_measured_says_so_without_inventing_a_state()
    {
        TestWorld world = new(signedInUser: null);

        DashboardViewModel dashboard = Create(world);

        Assert.All(dashboard.Cards, card =>
        {
            Assert.False(card.HasData);
            Assert.Null(card.Status);
            Assert.Equal("—", card.ValueText);
            Assert.Equal("Sin mediciones", card.TakenAtText);
            Assert.False(string.IsNullOrEmpty(card.UnitText));
        });
    }

    [Fact]
    public void A_check_up_that_omits_indicators_keeps_the_latest_reading_of_the_others()
    {
        TestWorld world = new("mara");
        Add(world, "mara", new ReadingInput(IndicatorType.HeartRate, 108));

        DashboardViewModel dashboard = Create(world);

        ResultCard(dashboard, "heartRate", "108", "Hoy, ", HealthStatus.Watch);
        // El resto sigue con el check-up anterior (de ayer).
        ResultCard(dashboard, "bloodSugar", "88", "Ayer, ", HealthStatus.Normal);
    }

    private static void ResultCard(DashboardViewModel dashboard, string id, string value, string whenPrefix, HealthStatus status)
    {
        IndicatorCardViewModel card = dashboard.Cards.Single(c => c.Id == id);
        Assert.Equal(value, card.ValueText);
        Assert.StartsWith(whenPrefix, card.TakenAtText);
        Assert.Equal(status, card.Status);
    }

    [Fact]
    public void Cards_use_the_spanish_indicator_names()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.Equal(
            ["Presión arterial", "Frecuencia cardíaca", "Glucosa", "Temperatura corporal", "Sueño", "Estado de ánimo"],
            dashboard.Cards.Select(c => c.Name));
    }

    // ---- Estado general arriba ----

    [Fact]
    public void The_overall_status_is_the_worst_current_reading_as_color_and_text()
    {
        DashboardViewModel ana = Create(new TestWorld("ana"));
        DashboardViewModel luis = Create(new TestWorld("luis"));

        Assert.Equal(HealthStatus.Normal, ana.OverallStatus);
        Assert.Equal("Estado general: Normal", ana.OverallText);
        Assert.Equal(HealthStatus.Critical, luis.OverallStatus);
        Assert.Equal("Estado general: Crítico", luis.OverallText);
    }

    [Fact]
    public void The_overall_status_follows_a_new_check_up()
    {
        TestWorld world = new("ana");
        Assert.Equal(HealthStatus.Normal, Create(world).OverallStatus);

        Add(world, "ana", new ReadingInput(IndicatorType.BloodSugar, 45));

        Assert.Equal(HealthStatus.Critical, Create(world).OverallStatus);
    }

    [Fact]
    public void Without_data_there_is_no_state_and_the_summary_invites_the_first_check_up()
    {
        DashboardViewModel dashboard = Create(new TestWorld(signedInUser: null));

        Assert.False(dashboard.HasData);
        Assert.Null(dashboard.OverallStatus);
        Assert.Equal("Sin mediciones todavía", dashboard.OverallText);
        Assert.Contains("primer check-up", dashboard.SummaryText);
    }

    [Fact]
    public void The_summary_says_what_needs_attention_and_when_the_last_check_up_was()
    {
        DashboardViewModel luis = Create(new TestWorld("luis"));
        DashboardViewModel ana = Create(new TestWorld("ana"));

        Assert.StartsWith("1 crítico, 3 en vigilancia", luis.SummaryText);
        Assert.Contains("Último check-up: Ayer, ", luis.SummaryText);
        Assert.StartsWith("Todos los indicadores en rango", ana.SummaryText);
    }

    [Fact]
    public void The_summary_counts_measured_indicators_when_some_were_never_measured()
    {
        TestWorld world = new(signedInUser: null);
        world.Session.SignIn(new SessionUser(Guid.NewGuid(), "nueva", "Nueva Persona"));
        world.Store.AddCheckUp(world.Session.CurrentUser!.Id, [new ReadingInput(IndicatorType.HeartRate, 70)],
            world.Evaluator.Evaluate([new ReadingInput(IndicatorType.HeartRate, 70)]));

        DashboardViewModel dashboard = Create(world);

        Assert.StartsWith("1 de 6 indicadores medidos, todos en rango", dashboard.SummaryText);
        Assert.Contains("Último check-up: Hoy, ", dashboard.SummaryText);
    }

    // ---- Aviso en críticos ----

    [Fact]
    public void A_critical_indicator_raises_the_warning_naming_it()
    {
        DashboardViewModel luis = Create(new TestWorld("luis"));

        Assert.True(luis.HasCritical);
        Assert.Equal("Glucosa", luis.CriticalNames);
        Assert.False(Create(new TestWorld("ana")).HasCritical);
    }

    // ---- Check-up de hoy pendiente (P2-REMIND) ----

    [Fact]
    public void Without_a_check_up_today_the_dashboard_says_it_is_pending()
    {
        TestWorld world = new("ana");
        world.ReminderSettings.Settings = new CheckUpReminderSettings(true, new TimeOnly(9, 0));

        DashboardViewModel dashboard = Create(world);

        Assert.True(dashboard.IsCheckUpPending);
        Assert.Contains("09:00", dashboard.PendingCheckUpText);
    }

    [Fact]
    public void With_a_check_up_today_there_is_no_pending_banner()
    {
        TestWorld world = new("ana");
        Add(world, "ana", new ReadingInput(IndicatorType.HeartRate, 70));

        Assert.False(Create(world).IsCheckUpPending);
    }

    [Fact]
    public void Without_a_reminder_the_pending_banner_points_to_the_profile()
    {
        DashboardViewModel dashboard = Create(new TestWorld("ana"));

        Assert.True(dashboard.IsCheckUpPending);
        Assert.Contains("perfil", dashboard.PendingCheckUpText);
    }

    [Fact]
    public void Without_a_session_there_is_no_pending_banner()
    {
        Assert.False(Create(new TestWorld(signedInUser: null)).IsCheckUpPending);
    }

    // ---- Botón "Nuevo check-up" ----

    [Fact]
    public void The_new_check_up_button_opens_the_check_up_form()
    {
        TestWorld world = new("ana");
        DashboardViewModel dashboard = Create(world);

        dashboard.NewCheckUpCommand.Execute(null);

        Assert.IsType<CheckUpViewModel>(world.Navigation.CurrentPage);
    }

    // ---- Resto ----

    [Fact]
    public void Greets_by_first_name_and_without_a_session_just_says_hello()
    {
        Assert.Equal("Hola, Ana", Create(new TestWorld("ana")).Greeting);
        Assert.Equal("Hola", Create(new TestWorld(signedInUser: null)).Greeting);
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
