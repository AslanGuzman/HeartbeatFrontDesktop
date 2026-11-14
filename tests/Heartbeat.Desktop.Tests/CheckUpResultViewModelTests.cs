using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class CheckUpResultViewModelTests
{
    /// <summary>Guarda un check-up desde el formulario y devuelve la pantalla de resultado a la que se navega.</summary>
    private static CheckUpResultViewModel Save(TestWorld world, string hr = "70", string sugar = "90", string sleep = "7", string temp = "36.6")
    {
        CheckUpViewModel form = world.NewCheckUp();
        form.Systolic = "120";
        form.Diastolic = "78";
        form.HeartRate = hr;
        form.BloodSugar = sugar;
        form.Temperature = temp;
        form.SleepHours = sleep;
        form.SelectMoodCommand.Execute(4);
        form.SaveCommand.Execute(null);
        return world.Result;
    }

    // ---- Navegación ----

    [Fact]
    public void Saving_a_check_up_opens_the_result_screen_with_that_check_up()
    {
        TestWorld world = new("mara");

        CheckUpResultViewModel result = Save(world);

        Assert.Equal([typeof(CheckUpResultViewModel)], world.Navigation.Visited);
        Assert.Equal(6, result.Readings.Count);
        Assert.Equal(HealthStatus.Normal, result.OverallStatus);
    }

    [Fact]
    public void The_result_belongs_to_the_check_up_section_of_the_menu()
    {
        Assert.Equal(typeof(CheckUpViewModel), Save(new TestWorld("mara")).NavigationSection);
    }

    [Fact]
    public void New_check_up_and_history_buttons_navigate()
    {
        TestWorld world = new("mara");
        CheckUpResultViewModel result = Save(world);

        result.NewCheckUpCommand.Execute(null);
        Assert.IsType<CheckUpViewModel>(world.Navigation.CurrentPage);

        result = Save(world);
        result.ShowHistoryCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(world.Navigation.CurrentPage);
    }

    // ---- Estado y etiqueta por lectura con color ----

    [Fact]
    public void Every_reading_has_its_status_name_and_label_so_color_is_never_the_only_signal()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), hr: "108", sugar: "45");

        Assert.All(result.Readings, r =>
        {
            Assert.Contains(r.StatusText, new[] { "Normal", "Vigilancia", "Crítico" });
            Assert.False(string.IsNullOrWhiteSpace(r.Label));
            Assert.False(string.IsNullOrWhiteSpace(r.ValueText));
        });
        Assert.Equal("Vigilancia", result.Readings.Single(r => r.Id == "heartRate").StatusText);
        Assert.Equal("Crítico", result.Readings.Single(r => r.Id == "bloodSugar").StatusText);
    }

    [Fact]
    public void The_overall_status_is_the_worst_reading()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), hr: "108", sugar: "45");

        Assert.Equal(HealthStatus.Critical, result.OverallStatus);
        Assert.Equal("Estado general: Crítico", result.OverallText);
    }

    // ---- Guía para cada estado no normal ----

    [Fact]
    public void Every_non_normal_reading_has_guidance_and_normal_ones_do_not()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), hr: "108", sugar: "45");

        foreach (ResultReadingViewModel reading in result.Readings)
        {
            if (reading.Status == HealthStatus.Normal)
            {
                Assert.False(reading.HasGuidance, reading.Name);
                Assert.True(reading.IsNormal);
            }
            else
            {
                Assert.True(reading.HasGuidance, reading.Name);
                Assert.All(reading.GuidanceItems, g => Assert.False(string.IsNullOrWhiteSpace(g)));
            }
        }
    }

    [Fact]
    public void Guidance_comes_from_the_shared_evaluator()
    {
        TestWorld world = new("mara");
        CheckUpResultViewModel result = Save(world, sugar: "45");

        ReadingEvaluation expected = world.Evaluator.Evaluate(new ReadingInput(IndicatorType.BloodSugar, 45));
        Assert.Equal(expected.Guidance, result.Readings.Single(r => r.Id == "bloodSugar").GuidanceItems);
    }

    // ---- Escalamiento destacado en críticos ----

    [Fact]
    public void A_critical_check_up_shows_the_escalation_with_who_to_contact_and_which_indicators()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), sugar: "45", temp: "40");

        Assert.True(result.HasEscalation);
        EscalationViewModel escalation = result.Escalation!;
        Assert.Equal("Resultado crítico", escalation.Title);
        Assert.Contains("oficial médico de la tripulación", escalation.Contact);
        Assert.Contains("Glucosa", escalation.Indicators);
        Assert.Contains("Temperatura corporal", escalation.Indicators);
        Assert.DoesNotContain("Sueño", escalation.Indicators);
        Assert.Contains("cirujanos de vuelo", escalation.Message);
    }

    [Fact]
    public void A_check_up_without_critical_readings_has_no_escalation()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), hr: "108");

        Assert.False(result.HasEscalation);
        Assert.Null(result.Escalation);
    }

    [Fact]
    public void Watch_readings_show_a_reminder_but_not_the_escalation()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"), hr: "108");

        Assert.NotNull(result.WatchNotice);
        Assert.Contains("próximo check-up", result.WatchNotice);
    }

    [Fact]
    public void A_normal_check_up_has_neither_escalation_nor_reminder()
    {
        CheckUpResultViewModel result = Save(new TestWorld("mara"));

        Assert.Null(result.Escalation);
        Assert.Null(result.WatchNotice);
    }

    // ---- Botón «Enviar reporte a control de misión» ----

    [Fact]
    public void Sending_the_report_queues_a_report_message_attached_to_the_check_up_while_offline()
    {
        TestWorld world = new("mara", online: false);
        CheckUpResultViewModel result = Save(world, sugar: "45");
        Assert.True(result.SendReportCommand.CanExecute(null));

        result.SendReportCommand.Execute(null);

        MessageRecord report = Assert.Single(world.Store.GetMessages(TestWorld.IdOf("mara")));
        Assert.True(report.IsReport);
        Assert.False(report.IsSent);
        Assert.Contains("Crítico", report.Subject);
        Assert.Contains("Glucosa", report.Body);
        Assert.Contains("45", report.Body);
        Assert.Contains("en cola", result.ReportText);
        Assert.True(result.ReportSent);
        Assert.False(result.SendReportCommand.CanExecute(null));
    }

    [Fact]
    public void Online_the_report_goes_out_right_away()
    {
        TestWorld world = new("mara", online: true);
        CheckUpResultViewModel result = Save(world);

        result.SendReportCommand.Execute(null);

        Assert.Equal("Reporte enviado a control de misión.", result.ReportText);
        Assert.True(world.Store.GetMessages(TestWorld.IdOf("mara")).Single().IsSent);
    }

    [Fact]
    public void The_report_can_only_be_sent_once_per_check_up()
    {
        TestWorld world = new("mara", online: false);
        CheckUpResultViewModel result = Save(world);

        result.SendReportCommand.Execute(null);
        result.SendReportCommand.Execute(null);

        Assert.Single(world.Store.GetMessages(TestWorld.IdOf("mara")));
    }

    [Fact]
    public void Reopening_a_check_up_that_already_has_a_report_keeps_the_button_off()
    {
        TestWorld world = new("mara", online: false);
        CheckUpResultViewModel first = Save(world);
        first.SendReportCommand.Execute(null);
        CheckUpRecord record = world.Store.GetCheckUps(TestWorld.IdOf("mara")).First();

        CheckUpResultViewModel again = new(world.Navigation, world.Store, world.Session, world.Info, world.Presenter, world.Connectivity);
        again.Load(record);

        Assert.True(again.ReportSent);
        Assert.False(again.SendReportCommand.CanExecute(null));
    }

    [Fact]
    public void The_report_includes_the_note_when_there_is_one()
    {
        TestWorld world = new("mara", online: false);
        CheckUpViewModel form = world.NewCheckUp();
        form.HeartRate = "70";
        form.Note = "Mareo al levantarme";
        form.SaveCommand.Execute(null);

        world.Result.SendReportCommand.Execute(null);

        Assert.Contains("Mareo al levantarme", world.Store.GetMessages(TestWorld.IdOf("mara")).Single().Body);
    }

    [Fact]
    public void Each_check_up_gets_its_own_report()
    {
        TestWorld world = new("mara", online: false);
        Save(world).SendReportCommand.Execute(null);
        CheckUpResultViewModel second = Save(world);

        Assert.False(second.ReportSent);
        second.SendReportCommand.Execute(null);

        Assert.Equal(2, world.Store.GetMessages(TestWorld.IdOf("mara")).Count);
    }

    // ---- Resumen de guardado ----

    [Fact]
    public void The_summary_says_how_many_indicators_and_whether_it_was_sent()
    {
        CheckUpResultViewModel online = Save(new TestWorld("mara", online: true));
        CheckUpResultViewModel offline = Save(new TestWorld("mara", online: false));

        Assert.Contains("6 indicadores", online.SummaryText);
        Assert.Contains("Enviado a control de misión", online.SummaryText);
        Assert.Contains("Se sincronizará cuando haya enlace", offline.SummaryText);
    }

    [Fact]
    public void The_note_is_shown_on_the_result()
    {
        TestWorld world = new("mara");
        CheckUpViewModel form = world.NewCheckUp();
        form.HeartRate = "70";
        form.Note = "algo";
        form.SaveCommand.Execute(null);

        Assert.Equal("algo", world.Result.Note);
    }

    [Fact]
    public void The_info_button_of_a_reading_opens_the_panel_on_that_indicator()
    {
        TestWorld world = new("mara");
        CheckUpResultViewModel result = Save(world);

        result.ShowInfoCommand.Execute("heartRate");

        Assert.Equal(["heartRate"], world.Presenter.Shown);
    }

    [Fact]
    public void Load_needs_a_check_up()
    {
        TestWorld world = new("mara");
        CheckUpResultViewModel result = new(world.Navigation, world.Store, world.Session, world.Info, world.Presenter, world.Connectivity);

        Assert.Throws<ArgumentNullException>(() => result.Load(null!));
        Assert.False(result.SendReportCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("Normal", "Normal", "Normal")]
    [InlineData("Vigilancia", "Fast", "Vigilancia · Fast")]
    [InlineData("Crítico", "", "Crítico")]
    public void The_chip_repeats_the_label_only_when_it_adds_something(string status, string label, string expected)
    {
        ResultReadingViewModel row = new("x", "X", "1", HealthStatus.Watch, status, label, []);

        Assert.Equal(expected, row.ChipText);
    }
}
