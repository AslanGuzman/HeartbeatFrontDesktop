using System.Globalization;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class CheckUpViewModelTests
{
    private static CheckUpViewModel Create(TestWorld world) => world.NewCheckUp();

    private static void FillAll(CheckUpViewModel vm, string sys = "120", string dia = "78", string hr = "70", string sugar = "90",
        string temp = "36.6", string sleep = "7", int? mood = 4)
    {
        vm.Systolic = sys;
        vm.Diastolic = dia;
        vm.HeartRate = hr;
        vm.BloodSugar = sugar;
        vm.Temperature = temp;
        vm.SleepHours = sleep;
        if (mood is { } m)
        {
            vm.SelectMoodCommand.Execute(m);
        }
    }

    private static List<LocalCheckUp> Stored(TestWorld world)
    {
        using IServiceScope scope = world.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LocalDbContext>().CheckUps.Include(c => c.Readings).AsNoTracking().ToList();
    }

    private static LocalCheckUp Saved(TestWorld world) => Stored(world).Single(c => c.RecordedAt == TestWorld.Now);

    // ---- 1 a 6 indicadores ----

    [Fact]
    public void A_complete_check_up_saves_six_readings_as_one_session()
    {
        TestWorld world = new("mara");
        int before = Stored(world).Count;
        CheckUpViewModel vm = Create(world);
        FillAll(vm);

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Equal(6, world.Result.Readings.Count);
        Assert.All(world.Result.Readings, r => Assert.Equal(HealthStatus.Normal, r.Status));
        Assert.Equal(before + 1, Stored(world).Count);
        LocalCheckUp saved = Saved(world);
        Assert.Equal(6, saved.Readings.Count);
        Assert.All(saved.Readings, r => Assert.Equal(saved.Id, r.CheckUpId));
    }

    [Fact]
    public void Indicators_can_be_omitted_and_a_single_one_is_enough()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "72";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Equal("Frecuencia cardíaca", Assert.Single(world.Result.Readings).Name);
        Assert.Equal(IndicatorType.HeartRate, Assert.Single(Saved(world).Readings).Indicator);
        Assert.Contains("1 indicador", world.Result.SummaryText);
    }

    [Fact]
    public void Several_but_not_all_indicators_are_saved_as_given()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.Temperature = "36,8";
        vm.SleepHours = "6.5";
        vm.SelectMoodCommand.Execute(2);

        vm.SaveCommand.Execute(null);

        Assert.Equal(["Temperatura corporal", "Sueño", "Estado de ánimo"], world.Result.Readings.Select(r => r.Name));
        Assert.Contains("3 indicadores", world.Result.SummaryText);
    }

    [Fact]
    public void An_empty_form_saves_nothing_and_explains_that_one_indicator_is_needed()
    {
        TestWorld world = new("mara");
        int before = Stored(world).Count;
        CheckUpViewModel vm = Create(world);

        vm.SaveCommand.Execute(null);

        Assert.Contains("al menos un indicador", vm.Error);
        Assert.Empty(world.Navigation.Visited);
        Assert.Equal(before, Stored(world).Count);
    }

    [Theory]
    [InlineData("120", "")]
    [InlineData("", "80")]
    public void Blood_pressure_needs_both_values_or_none(string sys, string dia)
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.Systolic = sys;
        vm.Diastolic = dia;
        vm.HeartRate = "70";

        vm.SaveCommand.Execute(null);

        Assert.Contains("Presión arterial", vm.Error);
        Assert.Contains("sistólica y la diastólica", vm.Error);
        Assert.Empty(world.Navigation.Visited);
    }

    [Fact]
    public void Blood_pressure_is_saved_as_systolic_and_diastolic_of_one_reading()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.Systolic = "135";
        vm.Diastolic = "85";

        vm.SaveCommand.Execute(null);

        LocalReading reading = Saved(world).Readings.Single();
        Assert.Equal(135m, reading.Value);
        Assert.Equal(85m, reading.SecondaryValue);
        Assert.Equal(HealthStatus.Watch, reading.Status);
    }

    // ---- Sueño: teclado o deslizador ----

    [Fact]
    public void The_sleep_slider_writes_its_value_in_the_text_field()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        vm.SleepSlider = 6.5;

        Assert.Equal(6.5.ToString("0.#", CultureInfo.CurrentCulture), vm.SleepHours);
    }

    [Theory]
    [InlineData("7", 7.0)]
    [InlineData("6,5", 6.5)]
    [InlineData("6.3", 6.5)]
    [InlineData("6.2", 6.0)]
    public void Typing_the_sleep_moves_the_slider_to_the_nearest_half_hour(string typed, double expected)
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        vm.SleepHours = typed;

        Assert.Equal(expected, vm.SleepSlider);
    }

    [Fact]
    public void A_typed_sleep_value_that_is_not_a_multiple_of_half_an_hour_is_kept_as_typed()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);

        vm.SleepHours = "6.3";
        vm.SaveCommand.Execute(null);

        Assert.Equal(6.3m, Saved(world).Readings.Single().Value);
    }

    [Fact]
    public void Sleep_from_the_slider_is_saved()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);

        vm.SleepSlider = 8;
        vm.SaveCommand.Execute(null);

        Assert.Equal(8m, Saved(world).Readings.Single().Value);
    }

    // ---- Ánimo: 5 botones ----

    [Fact]
    public void Mood_has_five_buttons_from_very_bad_to_very_good()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        Assert.Equal([1, 2, 3, 4, 5], vm.MoodOptions.Select(o => o.Value));
        Assert.Equal("Muy mal", vm.MoodOptions[0].Label);
        Assert.Equal("Muy bien", vm.MoodOptions[4].Label);
    }

    [Fact]
    public void Choosing_a_mood_selects_only_that_button_and_choosing_it_again_omits_mood()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        vm.SelectMoodCommand.Execute(4);
        Assert.Equal(4, vm.Mood);
        Assert.Equal([4], vm.MoodOptions.Where(o => o.IsSelected).Select(o => o.Value));

        vm.SelectMoodCommand.Execute(2);
        Assert.Equal([2], vm.MoodOptions.Where(o => o.IsSelected).Select(o => o.Value));

        vm.SelectMoodCommand.Execute(2);
        Assert.Null(vm.Mood);
        Assert.DoesNotContain(vm.MoodOptions, o => o.IsSelected);
    }

    [Fact]
    public void Mood_is_not_saved_unless_a_button_was_chosen()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "70";

        vm.SaveCommand.Execute(null);

        Assert.DoesNotContain(Saved(world).Readings, r => r.Indicator == IndicatorType.Mood);
    }

    // ---- Validación con IHealthEvaluator.Validate ----

    [Theory]
    [InlineData("abc")]
    [InlineData("12 3")]
    public void A_non_numeric_value_saves_nothing_and_names_the_field(string bad)
    {
        TestWorld world = new("mara");
        int before = Stored(world).Count;
        CheckUpViewModel vm = Create(world);
        FillAll(vm, hr: bad);

        vm.SaveCommand.Execute(null);

        Assert.Contains("Frecuencia cardíaca", vm.Error);
        Assert.Empty(world.Navigation.Visited);
        Assert.Equal(before, Stored(world).Count);
    }

    [Fact]
    public void A_value_the_evaluator_rejects_is_not_saved_and_the_indicator_is_named()
    {
        TestWorld world = new("mara");
        int before = Stored(world).Count;
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "999";
        vm.BloodSugar = "5";

        vm.SaveCommand.Execute(null);

        Assert.Contains("Frecuencia cardíaca", vm.Error);
        Assert.Contains("Glucosa", vm.Error);
        Assert.Equal(before, Stored(world).Count);
    }

    [Fact]
    public void Every_problem_is_listed_at_once()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "abc";
        vm.Temperature = "99";

        vm.SaveCommand.Execute(null);

        Assert.Contains("Frecuencia cardíaca", vm.Error);
        Assert.Contains("Temperatura corporal", vm.Error);
    }

    [Theory]
    [InlineData("36,6")]
    [InlineData("36.6")]
    public void Accepts_a_decimal_comma_or_point(string temperature)
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.Temperature = temperature;

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Equal(36.6m, Saved(world).Readings.Single().Value);
    }

    // ---- Guardado local, pendiente y evaluación inmediata ----

    [Fact]
    public void Saving_stores_the_check_up_locally_as_pending_with_the_ranges_version_and_overall_status()
    {
        TestWorld world = new("mara", online: false);
        CheckUpViewModel vm = Create(world);
        FillAll(vm, sugar: "45");

        vm.SaveCommand.Execute(null);

        LocalCheckUp saved = Saved(world);
        Assert.Equal(SyncState.Pending, saved.SyncState);
        Assert.Null(saved.ReceivedAt);
        Assert.Equal(TestWorld.IdOf("mara"), saved.AstronautId);
        Assert.Equal(world.Evaluator.RangesVersion, saved.RangesVersion);
        Assert.Equal(HealthStatus.Critical, saved.OverallStatus);
        Assert.All(saved.Readings, r => Assert.False(string.IsNullOrEmpty(r.BandIds)));
        Assert.Equal(1, world.Store.PendingCount);
    }

    [Fact]
    public void The_result_is_evaluated_right_away_even_without_a_link()
    {
        TestWorld world = new("mara", online: false);
        CheckUpViewModel vm = Create(world);
        FillAll(vm, hr: "108");

        vm.SaveCommand.Execute(null);

        ResultReadingViewModel row = world.Result.Readings.Single(r => r.Name == "Frecuencia cardíaca");
        Assert.Equal(HealthStatus.Watch, row.Status);
        Assert.NotEmpty(row.GuidanceItems);
        Assert.Contains("cuando haya enlace", world.Result.SummaryText);
    }

    [Fact]
    public void Online_the_pending_check_up_goes_out_through_the_sync()
    {
        TestWorld world = new("mara", online: true);
        CheckUpViewModel vm = Create(world);
        FillAll(vm);

        vm.SaveCommand.Execute(null);

        Assert.Equal(0, world.Store.PendingCount);
        Assert.Equal(SyncState.Synced, Saved(world).SyncState);
        Assert.Contains("Enviado", world.Result.SummaryText);
    }

    [Fact]
    public void A_critical_reading_shows_the_escalation_notice()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);
        FillAll(vm, sugar: "45");

        vm.SaveCommand.Execute(null);

        Assert.Contains("oficial médico", world.Result.Escalation!.Contact);
        Assert.Equal("Crítico", world.Result.Readings.Single(r => r.Name == "Glucosa").StatusText);
    }

    [Fact]
    public void A_watch_reading_gives_guidance_but_no_escalation_to_the_medical_officer()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);
        FillAll(vm, hr: "108");

        vm.SaveCommand.Execute(null);

        Assert.Null(world.Result.Escalation);
    }

    [Fact]
    public void The_optional_note_is_saved_trimmed()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "70";
        vm.Note = "  Me sentí mareada al levantarme.  ";

        vm.SaveCommand.Execute(null);

        Assert.Equal("Me sentí mareada al levantarme.", Saved(world).Note);
    }

    [Fact]
    public void Without_a_note_none_is_stored()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "70";
        vm.Note = "   ";

        vm.SaveCommand.Execute(null);

        Assert.Null(Saved(world).Note);
    }

    [Fact]
    public void A_failed_save_keeps_what_was_typed()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);
        vm.HeartRate = "999";

        vm.SaveCommand.Execute(null);

        Assert.Equal("999", vm.HeartRate);
    }

    [Fact]
    public void Without_a_session_nothing_is_saved()
    {
        TestWorld world = new(signedInUser: null);
        CheckUpViewModel vm = Create(world);
        FillAll(vm);

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(world.Navigation.Visited);
    }

    [Fact]
    public void The_info_button_of_a_field_opens_the_panel_on_that_indicator()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        vm.ShowInfoCommand.Execute("bloodSugar");

        Assert.Equal(["bloodSugar"], world.Presenter.Shown);
    }

    [Fact]
    public void The_note_limit_follows_the_contract()
    {
        Assert.Equal(500, Create(new TestWorld()).NoteMaxLength);
    }
}
