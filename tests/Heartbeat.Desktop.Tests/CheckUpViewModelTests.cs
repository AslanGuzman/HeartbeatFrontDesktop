using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class CheckUpViewModelTests
{
    private static CheckUpViewModel Create(TestWorld world) =>
        new(world.Store, world.Session, world.Evaluator, world.Info, world.Presenter, world.Connectivity);

    private static void Fill(CheckUpViewModel vm, string sys = "120", string dia = "78", string hr = "70", string sugar = "90",
        string temp = "36.6", string sleep = "7", double mood = 4)
    {
        vm.Systolic = sys;
        vm.Diastolic = dia;
        vm.HeartRate = hr;
        vm.BloodSugar = sugar;
        vm.Temperature = temp;
        vm.SleepHours = sleep;
        vm.Mood = mood;
    }

    [Fact]
    public void A_complete_normal_check_up_is_saved_as_one_session_with_six_readings()
    {
        TestWorld world = new("mara");
        CheckUpViewModel vm = Create(world);
        Fill(vm);

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Null(vm.Escalation);
        Assert.Equal(6, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.Equal(HealthStatus.Normal, r.Status));
        CheckUpRecord_Latest(world, "mara", out int readings, out HealthStatus overall);
        Assert.Equal(6, readings);
        Assert.Equal(HealthStatus.Normal, overall);
    }

    private static void CheckUpRecord_Latest(TestWorld world, string user, out int readings, out HealthStatus overall)
    {
        var latest = world.Store.GetCheckUps(user)[0];
        readings = latest.Readings.Count;
        overall = latest.OverallStatus;
    }

    [Fact]
    public void A_critical_reading_shows_the_escalation_notice_and_the_critical_status()
    {
        CheckUpViewModel vm = Create(new TestWorld());
        Fill(vm, sugar: "45");

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Escalation);
        Assert.Contains("oficial médico", vm.Escalation);
        Assert.Equal(HealthStatus.Critical, vm.Results.Single(r => r.Name == "Glucosa").Status);
        Assert.Equal("Crítico", vm.Results.Single(r => r.Name == "Glucosa").StatusText);
    }

    [Fact]
    public void A_watch_reading_gives_guidance_but_no_escalation_to_the_medical_officer()
    {
        CheckUpViewModel vm = Create(new TestWorld());
        Fill(vm, hr: "108");

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Escalation);
        ReadingRowViewModel row = vm.Results.Single(r => r.Name == "Frecuencia cardíaca");
        Assert.Equal(HealthStatus.Watch, row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.Guidance));
    }

    [Theory]
    [InlineData("36,6")]
    [InlineData("36.6")]
    public void Accepts_a_decimal_comma_or_point(string temperature)
    {
        CheckUpViewModel vm = Create(new TestWorld());
        Fill(vm, temp: temperature);

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Equal(6, vm.Results.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12 3")]
    public void A_missing_or_non_numeric_value_saves_nothing(string bad)
    {
        TestWorld world = new("mara");
        int before = world.Store.GetCheckUps("mara").Count;
        CheckUpViewModel vm = Create(world);
        Fill(vm, hr: bad);

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Results);
        Assert.Equal(before, world.Store.GetCheckUps("mara").Count);
    }

    [Fact]
    public void A_value_outside_what_can_be_measured_is_rejected_naming_the_indicator()
    {
        TestWorld world = new("mara");
        int before = world.Store.GetCheckUps("mara").Count;
        CheckUpViewModel vm = Create(world);
        Fill(vm, hr: "999");

        vm.SaveCommand.Execute(null);

        Assert.Contains("Frecuencia cardíaca", vm.Error);
        Assert.Equal(before, world.Store.GetCheckUps("mara").Count);
    }

    [Fact]
    public void Offline_the_check_up_is_kept_pending_and_the_notice_says_so()
    {
        TestWorld world = new("mara", online: false);
        CheckUpViewModel vm = Create(world);
        Fill(vm);

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, world.Store.PendingCount);
        Assert.Contains("cuando haya enlace", vm.SavedText);
    }

    [Fact]
    public void Online_the_check_up_is_sent_right_away()
    {
        TestWorld world = new("mara", online: true);
        CheckUpViewModel vm = Create(world);
        Fill(vm);

        vm.SaveCommand.Execute(null);

        Assert.Equal(0, world.Store.PendingCount);
        Assert.Contains("enviado", vm.SavedText);
    }

    [Fact]
    public void Saving_again_replaces_the_previous_result_and_notices()
    {
        CheckUpViewModel vm = Create(new TestWorld());
        Fill(vm, sugar: "45");
        vm.SaveCommand.Execute(null);
        Assert.NotNull(vm.Escalation);

        Fill(vm);
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Escalation);
        Assert.Equal(6, vm.Results.Count);
    }

    [Fact]
    public void Without_a_session_nothing_is_saved()
    {
        TestWorld world = new(signedInUser: null);
        CheckUpViewModel vm = Create(world);
        Fill(vm);

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public void The_info_button_of_a_field_opens_the_panel_on_that_indicator()
    {
        TestWorld world = new();
        CheckUpViewModel vm = Create(world);

        vm.ShowInfoCommand.Execute("bloodSugar");

        Assert.Equal(["bloodSugar"], world.Presenter.Shown);
    }
}
