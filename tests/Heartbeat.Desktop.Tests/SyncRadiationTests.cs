using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Sync;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Las dosis de radiación (#51) también viajan en <c>/sync</c> y comparten accepted / rejected con los check-ups.</summary>
public sealed class SyncRadiationTests
{
    private static DateTimeOffset T(int minutes) => TestWorld.Now.AddMinutes(minutes);

    private static RadiationDoseRecord AddDose(SyncTestWorld w, string user, int minutesAgo, decimal msv = 0.5m, string? note = null)
    {
        DateTimeOffset previous = w.World.Clock.Now;
        w.World.Clock.Now = T(-minutesAgo);
        RadiationDoseRecord dose = w.World.Store.AddRadiationDose(w.Id(user), msv, note);
        w.World.Clock.Now = previous;
        return dose;
    }

    private static List<LocalRadiationDose> Doses(SyncTestWorld w) =>
        w.World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().RadiationDoses.ToList();

    [Fact]
    public async Task Pending_doses_are_sent_in_the_same_call_and_marked_synced_with_the_server_time()
    {
        using SyncTestWorld w = new();
        RadiationDoseRecord dose = AddDose(w, "ana", 5, 1.25m, "paseo");
        w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(9)));

        SyncResult result = await w.Engine.SyncNowAsync();

        RadiationDoseDto sent = Assert.Single(w.Api.Syncs.Single().RadiationDoses!);
        Assert.Equal(dose.Id, sent.Id);
        Assert.Equal(1.25m, sent.DoseMsv);
        Assert.Equal("paseo", sent.Note);
        Assert.Equal(T(-5), sent.RecordedAt);
        Assert.Null(sent.ReceivedAt);
        Assert.Equal(1, result.Report!.RadiationDosesSent);
        LocalRadiationDose stored = Assert.Single(Doses(w), d => d.Id == dose.Id);
        Assert.Equal(SyncState.Synced, stored.SyncState);
        Assert.Equal(T(9), stored.ReceivedAt);
        Assert.Equal(0, w.Engine.PendingCount);
    }

    [Fact]
    public async Task Doses_count_as_pending_for_the_astronaut_who_registered_them_and_only_for_them()
    {
        using SyncTestWorld w = new();
        AddDose(w, "ana", 5);
        AddDose(w, "luis", 5);

        Assert.Equal(1, w.Engine.PendingCount);
        Assert.Equal(1, w.World.Store.GetPendingCount(w.Id("ana")));

        await w.Engine.SyncNowAsync();

        Assert.Equal(0, w.Engine.PendingCount);
        Assert.Equal(1, w.World.Store.GetPendingCount(w.Id("luis")));
        Assert.Equal(SyncState.Pending, Doses(w).Single(d => d.AstronautId == w.Id("luis")).SyncState);
    }

    [Fact]
    public async Task A_rejected_dose_keeps_the_reason_is_not_resent_and_is_counted_as_rejected()
    {
        using SyncTestWorld w = new();
        RadiationDoseRecord dose = AddDose(w, "ana", 5);
        w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(
            T(1), [], [new SyncRejected(dose.Id, "Radiation dose cannot exceed 500 mSv per entry")], null, new SyncInbound([], []), false));

        SyncResult result = await w.Engine.SyncNowAsync();

        SyncRejection rejection = Assert.Single(result.Report!.Rejected);
        Assert.Equal("dosis de radiación", rejection.Kind);
        Assert.Equal("Radiation dose cannot exceed 500 mSv per entry", rejection.Reason);
        LocalRadiationDose stored = Doses(w).Single(d => d.Id == dose.Id);
        Assert.Equal(SyncState.Rejected, stored.SyncState);
        Assert.Equal("Radiation dose cannot exceed 500 mSv per entry", stored.SyncError);
        Assert.Equal(1, w.Engine.RejectedCount);
        Assert.Equal(0, w.Engine.PendingCount);

        w.Api.OnSyncAsync = null;
        await w.Engine.SyncNowAsync();
        Assert.All(w.Api.Syncs.Skip(1), s => Assert.Empty(s.RadiationDoses ?? []));
    }

    [Fact]
    public async Task Doses_go_in_batches_oldest_first_like_check_ups()
    {
        using SyncTestWorld w = new(batchSize: 2);
        RadiationDoseRecord third = AddDose(w, "ana", 30);
        RadiationDoseRecord first = AddDose(w, "ana", 50);
        RadiationDoseRecord second = AddDose(w, "ana", 40);

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal([2, 1], w.Api.Syncs.Select(s => s.RadiationDoses!.Count));
        Assert.Equal([first.Id, second.Id, third.Id], w.Api.Syncs.SelectMany(s => s.RadiationDoses!).Select(d => d.Id));
        Assert.Equal(3, result.Report!.RadiationDosesSent);
        Assert.Equal(0, w.Engine.PendingCount);
    }

    [Fact]
    public async Task Check_ups_messages_and_doses_travel_together_and_are_marked_by_their_own_ids()
    {
        using SyncTestWorld w = new();
        CheckUpRecord checkUp = w.AddCheckUp("ana", T(-3));
        MessageRecord message = w.World.Store.QueueMessage(w.Id("ana"), "hola");
        RadiationDoseRecord dose = AddDose(w, "ana", 2);

        SyncResult result = await w.Engine.SyncNowAsync();

        SyncRequest request = Assert.Single(w.Api.Syncs);
        Assert.Equal([checkUp.Id], request.CheckUps.Select(c => c.Id));
        Assert.Equal([message.Id], request.Messages.Select(m => m.Id));
        Assert.Equal([dose.Id], request.RadiationDoses!.Select(d => d.Id));
        Assert.Equal((1, 1, 1), (result.Report!.CheckUpsSent, result.Report.MessagesSent, result.Report.RadiationDosesSent));
        Assert.Equal(0, w.Engine.PendingCount);
    }

    [Fact]
    public async Task A_dose_the_server_does_not_confirm_stays_pending_and_the_attempt_says_so()
    {
        using SyncTestWorld w = new();
        RadiationDoseRecord dose = AddDose(w, "ana", 2);
        w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(T(1), [], [], null, new SyncInbound([], []), false));

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SyncState.Pending, Doses(w).Single(d => d.Id == dose.Id).SyncState);
    }

    [Fact]
    public async Task Open_screens_refresh_when_doses_are_synced()
    {
        using SyncTestWorld w = new();
        AddDose(w, "ana", 2);
        int changes = 0;
        w.World.Store.Changed += (_, _) => changes++;

        await w.Engine.SyncNowAsync();

        Assert.True(changes >= 1);
    }
}
