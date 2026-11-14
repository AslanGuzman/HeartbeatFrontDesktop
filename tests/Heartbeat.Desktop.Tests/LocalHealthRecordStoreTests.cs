using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class LocalHealthRecordStoreTests
{
    // Sin enlace: así lo pendiente no se envía solo y se puede comprobar el estado pendiente.
    private readonly TestWorld _world = new(signedInUser: null, online: false);
    private readonly Guid _astronaut = Guid.NewGuid();

    private static List<ReadingInput> Inputs(params (IndicatorType, decimal)[] values) =>
        values.Select(v => new ReadingInput(v.Item1, v.Item2)).ToList();

    private CheckUpRecord Add(List<ReadingInput> inputs, string? note = null) =>
        _world.Store.AddCheckUp(_astronaut, inputs, _world.Evaluator.Evaluate(inputs), note);

    [Fact]
    public void A_saved_check_up_comes_back_with_its_readings_evaluated_and_pending()
    {
        List<ReadingInput> inputs = [new(IndicatorType.BloodPressure, 135, 85), new(IndicatorType.HeartRate, 70)];

        Add(inputs, "nota");

        CheckUpRecord loaded = Assert.Single(_world.Store.GetCheckUps(_astronaut));
        Assert.False(loaded.IsSynced);
        Assert.Equal("nota", loaded.Note);
        Assert.Equal(TestWorld.Now, loaded.RecordedAt);
        Assert.Equal(HealthStatus.Watch, loaded.OverallStatus);
        Assert.Equal([IndicatorType.BloodPressure, IndicatorType.HeartRate], loaded.Readings.Select(r => r.Input.Indicator));
        Assert.Equal(85m, loaded.Readings[0].Input.SecondaryValue);
        Assert.Equal(HealthStatus.Watch, loaded.Readings[0].Evaluation.Status);
        Assert.Equal(HealthStatus.Normal, loaded.Readings[1].Evaluation.Status);
    }

    [Fact]
    public void Data_is_kept_per_astronaut()
    {
        Add(Inputs((IndicatorType.HeartRate, 70)));

        Assert.Empty(_world.Store.GetCheckUps(Guid.NewGuid()));
    }

    [Fact]
    public void Check_ups_come_back_newest_first()
    {
        Add(Inputs((IndicatorType.HeartRate, 60)));
        _world.Clock.Now = _world.Clock.Now.AddHours(1);
        Add(Inputs((IndicatorType.HeartRate, 80)));

        Assert.Equal([80m, 60m], _world.Store.GetCheckUps(_astronaut).Select(c => c.Readings[0].Input.Value));
    }

    [Fact]
    public void Mark_all_synced_flips_pending_check_ups_and_messages_and_sets_the_last_sync()
    {
        Add(Inputs((IndicatorType.HeartRate, 70)));
        _world.Store.QueueMessage(_astronaut, "hola");
        int before = _world.Store.PendingCount;
        DateTimeOffset at = TestWorld.Now.AddMinutes(5);

        int sent = _world.Store.MarkAllSynced(at);

        Assert.Equal(2, sent);
        Assert.True(before >= 2);
        Assert.Equal(0, _world.Store.PendingCount);
        Assert.Equal(at, _world.Store.LastSyncedAt);
        Assert.True(_world.Store.GetCheckUps(_astronaut).Single().IsSynced);
        Assert.True(_world.Store.GetMessages(_astronaut).Single().IsSent);
        Assert.Equal(0, _world.Store.MarkAllSynced(at.AddMinutes(1)));
    }

    [Fact]
    public void A_check_up_needs_between_one_and_six_distinct_readings()
    {
        Assert.Throws<ArgumentException>(() => Add([]));
        Assert.Throws<ArgumentException>(() => Add(Inputs((IndicatorType.HeartRate, 70), (IndicatorType.HeartRate, 71))));
    }

    [Fact]
    public void The_evaluation_must_match_the_readings()
    {
        List<ReadingInput> inputs = Inputs((IndicatorType.HeartRate, 70), (IndicatorType.Sleep, 7));
        CheckUpEvaluation other = _world.Evaluator.Evaluate(Inputs((IndicatorType.HeartRate, 70)));

        Assert.Throws<ArgumentException>(() => _world.Store.AddCheckUp(_astronaut, inputs, other));
    }

    [Fact]
    public void A_note_over_the_limit_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Add(Inputs((IndicatorType.HeartRate, 70)), new string('x', 501)));
        Add(Inputs((IndicatorType.HeartRate, 70)), new string('x', 500));
    }

    [Fact]
    public void Messages_get_a_subject_from_their_first_line_and_stay_in_the_queue_until_synced()
    {
        _world.Store.QueueMessage(_astronaut, "Primera línea\nsegunda línea");

        MessageRecord message = Assert.Single(_world.Store.GetMessages(_astronaut));
        Assert.Equal("Primera línea\nsegunda línea", message.Body);
        Assert.False(message.IsSent);
    }

    [Fact]
    public void A_message_over_the_limit_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _world.Store.QueueMessage(_astronaut, new string('x', 4001)));
        Assert.Throws<ArgumentException>(() => _world.Store.QueueMessage(_astronaut, "   "));
    }

    [Fact]
    public void Changed_is_raised_after_every_write()
    {
        int raised = 0;
        _world.Store.Changed += (_, _) => raised++;

        Add(Inputs((IndicatorType.HeartRate, 70)));
        _world.Store.QueueMessage(_astronaut, "x");
        _world.Store.MarkAllSynced(TestWorld.Now);

        Assert.Equal(3, raised);
    }

    [Fact]
    public void The_seed_only_runs_for_astronauts_without_check_ups()
    {
        int before = _world.Store.GetCheckUps(TestWorld.IdOf("ana")).Count;

        Heartbeat.Desktop.Demo.DemoDataSeeder.Seed(_world.Services);

        Assert.Equal(before, _world.Store.GetCheckUps(TestWorld.IdOf("ana")).Count);
        Assert.Equal(3, before);
    }
}
