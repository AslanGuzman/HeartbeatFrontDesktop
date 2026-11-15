using System.Net;
using System.Net.Http;

using Heartbeat.Client;
using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Sync;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly SyncTestWorld _w = new();

    public void Dispose() => _w.Dispose();

    private static DateTimeOffset T(int minutes) => TestWorld.Now.AddMinutes(minutes);

    // ================= Marcar synced solo lo aceptado =================

    [Fact]
    public async Task Pending_check_ups_are_sent_and_marked_synced_with_the_server_time()
    {
        _w.AddCheckUp("ana", T(-3));
        _w.AddCheckUp("ana", T(-2));
        _w.AddCheckUp("ana", T(-1));
        DateTimeOffset serverTime = T(5);
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, serverTime));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.Equal(3, result.Report!.CheckUpsSent);
        Assert.All(_w.CheckUps().Where(c => c.AstronautId == _w.Id("ana") && c.RecordedAt >= T(-3) && c.RecordedAt <= T(-1)), c =>
        {
            Assert.Equal(SyncState.Synced, c.SyncState);
            Assert.Equal(serverTime, c.ReceivedAt);
        });
        Assert.Equal(0, _w.World.Store.GetPendingCount(_w.Id("ana")));
    }

    [Fact]
    public async Task Only_the_ids_in_accepted_become_synced()
    {
        CheckUpRecord a = _w.AddCheckUp("ana", T(-3));
        CheckUpRecord b = _w.AddCheckUp("ana", T(-2));
        CheckUpRecord c = _w.AddCheckUp("ana", T(-1));
        _w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(T(1), [a.Id, c.Id], [], null, new SyncInbound([], []), false));

        SyncResult result = await _w.Engine.SyncNowAsync();

        // El servidor no dijo nada de b: sigue pendiente, y el intento lo dice en vez de darlo por bueno.
        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SyncState.Synced, StateOf(a.Id));
        Assert.Equal(SyncState.Pending, StateOf(b.Id));
        Assert.Equal(SyncState.Synced, StateOf(c.Id));
    }

    private SyncState StateOf(Guid checkUpId) => _w.CheckUps().Single(c => c.Id == checkUpId).SyncState;

    [Fact]
    public async Task Accepted_ids_that_were_not_sent_are_ignored()
    {
        CheckUpRecord mine = _w.AddCheckUp("ana", T(-1));
        CheckUpRecord others = _w.AddCheckUp("luis", T(-1));
        _w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(T(1), [mine.Id, others.Id, Guid.NewGuid()], [], null, new SyncInbound([], []), false));

        await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncState.Synced, StateOf(mine.Id));
        Assert.Equal(SyncState.Pending, StateOf(others.Id));
    }

    // ================= Rechazados con motivo =================

    [Fact]
    public async Task Rejected_items_are_marked_rejected_with_the_reason_and_listed_in_the_report()
    {
        CheckUpRecord ok = _w.AddCheckUp("ana", T(-2));
        CheckUpRecord bad = _w.AddCheckUp("ana", T(-1), note: "mala");
        _w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(
            T(1), [ok.Id], [new SyncRejected(bad.Id, "Reading value outside valid range")], null, new SyncInbound([], []), false));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        SyncRejection rejection = Assert.Single(result.Report!.Rejected);
        Assert.Equal(bad.Id, rejection.Id);
        Assert.Equal("check-up", rejection.Kind);
        Assert.Equal("Reading value outside valid range", rejection.Reason);
        LocalCheckUp stored = _w.CheckUps().Single(c => c.Id == bad.Id);
        Assert.Equal(SyncState.Rejected, stored.SyncState);
        Assert.Equal("Reading value outside valid range", stored.SyncError);
        Assert.Null(stored.ReceivedAt);
        Assert.Equal(1, _w.World.Store.GetRejectedCount(_w.Id("ana")));
    }

    [Fact]
    public async Task A_rejected_item_is_not_sent_again_and_does_not_count_as_pending()
    {
        CheckUpRecord bad = _w.AddCheckUp("ana", T(-1));
        _w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(T(1), [], [new SyncRejected(bad.Id, "no")], null, new SyncInbound([], []), false));
        await _w.Engine.SyncNowAsync();
        _w.Api.OnSyncAsync = null;

        await _w.Engine.SyncNowAsync();

        Assert.DoesNotContain(_w.Api.Syncs.Skip(1), r => r.CheckUps.Any(c => c.Id == bad.Id));
        Assert.Equal(0, _w.World.Store.GetPendingCount(_w.Id("ana")));
        Assert.Equal(0, _w.Engine.PendingCount);
        Assert.Equal(1, _w.Engine.RejectedCount);
    }

    [Fact]
    public async Task A_rejected_message_is_marked_rejected_with_its_reason()
    {
        MessageRecord message = _w.World.Store.QueueMessage(_w.Id("ana"), "hola");
        _w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(T(1), [], [new SyncRejected(message.Id, "Message id is already in use")], null, new SyncInbound([], []), false));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal("mensaje", result.Report!.Rejected.Single().Kind);
        MessageRecord stored = _w.World.Store.GetMessages(_w.Id("ana")).Single();
        Assert.True(stored.IsRejected);
        Assert.Equal("Message id is already in use", stored.SyncError);
        Assert.False(stored.IsSent);
    }

    // ================= Lotes de hasta 500, del más viejo al más nuevo =================

    [Fact]
    public void The_default_options_are_60_seconds_and_batches_of_500()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), SyncOptions.Default.Interval);
        Assert.Equal(500, SyncOptions.Default.BatchSize);
    }

    [Fact]
    public async Task Check_ups_go_in_batches_oldest_first()
    {
        using SyncTestWorld w = new(batchSize: 2);
        // Se guardan desordenados a propósito.
        CheckUpRecord c3 = w.AddCheckUp("ana", T(-30));
        CheckUpRecord c5 = w.AddCheckUp("ana", T(-10));
        CheckUpRecord c1 = w.AddCheckUp("ana", T(-50));
        CheckUpRecord c4 = w.AddCheckUp("ana", T(-20));
        CheckUpRecord c2 = w.AddCheckUp("ana", T(-40));

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.Equal(3, w.Api.Syncs.Count);
        Assert.Equal([2, 2, 1], w.Api.Syncs.Select(s => s.CheckUps.Count));
        Assert.Equal([c1.Id, c2.Id, c3.Id, c4.Id, c5.Id], w.Api.Syncs.SelectMany(s => s.CheckUps).Select(c => c.Id));
        Assert.Equal(5, result.Report!.CheckUpsSent);
        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));
    }

    [Fact]
    public async Task A_full_batch_of_500_is_the_most_a_single_call_carries()
    {
        using SyncTestWorld w = new();
        ReadingInput[] input = [new(IndicatorType.HeartRate, 70)];
        CheckUpEvaluation evaluation = w.World.Evaluator.Evaluate(input);
        for (int i = 0; i < 501; i++)
        {
            w.World.Clock.Now = T(-1000 + i);
            w.World.Store.AddCheckUp(w.Id("ana"), input, evaluation);
        }

        w.World.Clock.Now = TestWorld.Now;
        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal([500, 1], w.Api.Syncs.Select(s => s.CheckUps.Count));
        Assert.Equal(501, result.Report!.CheckUpsSent);
    }

    [Fact]
    public async Task Each_batch_can_be_checked_against_the_oldest_pending_at_that_moment()
    {
        using SyncTestWorld w = new(batchSize: 2);
        for (int i = 0; i < 4; i++)
        {
            w.AddCheckUp("ana", T(-40 + i));
        }

        await w.Engine.SyncNowAsync();

        Assert.True(w.Api.Syncs[0].CheckUps.Max(c => c.RecordedAt) < w.Api.Syncs[1].CheckUps.Min(c => c.RecordedAt));
    }

    [Fact]
    public async Task A_report_does_not_travel_before_the_check_up_it_attaches()
    {
        using SyncTestWorld w = new(batchSize: 1);
        CheckUpRecord first = w.AddCheckUp("ana", T(-20));
        CheckUpRecord second = w.AddCheckUp("ana", T(-10));
        w.World.Store.QueueReport(w.Id("ana"), second.Id, "Reporte", "cuerpo");
        w.World.Store.QueueMessage(w.Id("ana"), "mensaje libre");

        await w.Engine.SyncNowAsync();

        Assert.Equal(2, w.Api.Syncs.Count);
        // Primer lote: el check-up viejo y el mensaje libre; el reporte espera a su check-up.
        Assert.Equal([first.Id], w.Api.Syncs[0].CheckUps.Select(c => c.Id));
        Assert.Equal(["mensaje libre"], w.Api.Syncs[0].Messages.Select(m => m.Body));
        Assert.Equal([second.Id], w.Api.Syncs[1].CheckUps.Select(c => c.Id));
        MessageDto report = Assert.Single(w.Api.Syncs[1].Messages);
        Assert.Equal(MessageKind.Report, report.Kind);
        Assert.Equal(second.Id, report.CheckUpId);
        Assert.Equal(0, w.World.Store.GetPendingCount(w.Id("ana")));
    }

    // ================= serverTime → lastPulledAt =================

    [Fact]
    public async Task The_server_time_is_stored_as_the_next_last_pulled_at()
    {
        _w.AddCheckUp("ana", T(-1));
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(7)));

        await _w.Engine.SyncNowAsync();

        Assert.Equal(T(7), _w.Cursor("ana")!.LastPulledAt);
        Assert.Equal(T(7), _w.Engine.LastSyncAt);
    }

    [Fact]
    public async Task The_first_sync_sends_a_null_last_pulled_at_and_the_next_one_the_stored_server_time()
    {
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(3)));

        await _w.Engine.SyncNowAsync();
        await _w.Engine.SyncNowAsync();

        Assert.Null(_w.Api.Syncs[0].LastPulledAt);
        Assert.Equal(T(3), _w.Api.Syncs[1].LastPulledAt);
    }

    [Fact]
    public async Task The_cursor_is_per_astronaut()
    {
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(3)));
        await _w.Engine.SyncNowAsync();
        _w.World.Session.SignOut();
        _w.World.Session.SignIn(DemoAccountsFor("luis"));

        Assert.Null(_w.Engine.LastSyncAt);
        await _w.Engine.SyncNowAsync();

        Assert.Null(_w.Api.Syncs[1].LastPulledAt);
        Assert.Equal(T(3), _w.Engine.LastSyncAt);
    }

    private static SessionUser DemoAccountsFor(string user) => Heartbeat.Desktop.Demo.DemoAccounts.All.First(a => a.UserName == user);

    [Fact]
    public async Task Last_sync_is_never_for_an_astronaut_that_has_not_synced()
    {
        Assert.Null(_w.Engine.LastSyncAt);
        await _w.Engine.SyncNowAsync();
        Assert.NotNull(_w.Engine.LastSyncAt);
    }

    // ================= Qué se envía =================

    [Fact]
    public async Task The_request_carries_the_device_id_the_ranges_version_and_the_full_check_up()
    {
        CheckUpRecord record = _w.AddCheckUp("ana", T(-1), 108, "con nota");

        await _w.Engine.SyncNowAsync();

        SyncRequest request = Assert.Single(_w.Api.Syncs);
        Assert.Equal("equipo-1", request.DeviceId);
        Assert.Equal(ReferenceRanges.Version, request.RangesVersion);
        CheckUpDto dto = Assert.Single(request.CheckUps);
        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(T(-1), dto.RecordedAt);
        Assert.Null(dto.ReceivedAt);
        Assert.Equal(HealthStatus.Watch, dto.OverallStatus);
        Assert.Equal(ReferenceRanges.Version, dto.RangesVersion);
        Assert.Equal("con nota", dto.Note);
        ReadingDto reading = Assert.Single(dto.Readings);
        Assert.Equal(IndicatorType.HeartRate, reading.Indicator);
        Assert.Equal(108m, reading.Value);
        Assert.Equal(HealthStatus.Watch, reading.Status);
        Assert.Equal(["hr-watch-high"], reading.BandIds);
        Assert.Equal(ReadingSource.Manual, reading.Source);
    }

    [Fact]
    public async Task Blood_pressure_travels_with_both_values_and_every_band()
    {
        ReadingInput[] bp = [new(IndicatorType.BloodPressure, 185, 95)];
        _w.World.Store.AddCheckUp(_w.Id("ana"), bp, _w.World.Evaluator.Evaluate(bp));

        await _w.Engine.SyncNowAsync();

        ReadingDto reading = _w.Api.Syncs.Single().CheckUps.Single().Readings.Single();
        Assert.Equal(185m, reading.Value);
        Assert.Equal(95m, reading.SecondaryValue);
        Assert.Equal(2, reading.BandIds.Count);
    }

    [Fact]
    public async Task Messages_go_to_mission_control_with_their_kind_and_attached_check_up()
    {
        CheckUpRecord checkUp = _w.AddCheckUp("ana", T(-5));
        MessageRecord message = _w.World.Store.QueueMessage(_w.Id("ana"), "Necesito ayuda");
        MessageRecord report = _w.World.Store.QueueReport(_w.Id("ana"), checkUp.Id, "Asunto", "Detalle");

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(2, result.Report!.MessagesSent);
        Assert.All(_w.Api.Syncs.Single().Messages, m => Assert.Equal(MessageDirection.ToMissionControl, m.Direction));
        MessageDto sentReport = _w.Api.Syncs.Single().Messages.Single(m => m.Id == report.Id);
        Assert.Equal(MessageKind.Report, sentReport.Kind);
        Assert.Equal(checkUp.Id, sentReport.CheckUpId);
        Assert.Equal("Asunto", sentReport.Subject);
        Assert.Equal(MessageKind.Message, _w.Api.Syncs.Single().Messages.Single(m => m.Id == message.Id).Kind);
        Assert.All(_w.World.Store.GetMessages(_w.Id("ana")), m => Assert.True(m.IsSent));
    }

    [Fact]
    public async Task Only_the_astronaut_with_the_open_session_is_synced()
    {
        CheckUpRecord mine = _w.AddCheckUp("ana", T(-1));
        CheckUpRecord others = _w.AddCheckUp("luis", T(-1));

        await _w.Engine.SyncNowAsync();

        Assert.Equal([mine.Id], _w.Api.Syncs.SelectMany(s => s.CheckUps).Select(c => c.Id));
        Assert.Equal(SyncState.Pending, StateOf(others.Id));
    }

    [Fact]
    public async Task Syncing_again_sends_nothing_that_was_already_sent_but_still_pulls()
    {
        _w.AddCheckUp("ana", T(-1));
        await _w.Engine.SyncNowAsync();

        await _w.Engine.SyncNowAsync();

        Assert.Equal(2, _w.Api.Syncs.Count);
        Assert.Empty(_w.Api.Syncs[1].CheckUps);
        Assert.Empty(_w.Api.Syncs[1].Messages);
    }

    [Fact]
    public async Task With_nothing_pending_a_sync_still_happens_to_pull_what_is_new()
    {
        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.Single(_w.Api.Syncs);
    }

    // ================= Mensajes de control de misión =================

    [Fact]
    public async Task Inbound_messages_are_stored_once_even_if_the_server_repeats_them()
    {
        Guid id = Guid.NewGuid();
        MessageDto inbound = new(id, MessageDirection.ToAstronaut, MessageKind.Message, null, "Descansa", "Descansa 24 h", T(-2), T(1), null, "Dr. Okafor");
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), inbound: new SyncInbound([inbound], [])));

        SyncResult first = await _w.Engine.SyncNowAsync();
        SyncResult second = await _w.Engine.SyncNowAsync();

        Assert.Equal(1, first.Report!.InboundMessages);
        Assert.Equal(0, second.Report!.InboundMessages);
        LocalMessage stored = Assert.Single(_w.Messages(), m => m.Direction == MessageDirection.ToAstronaut);
        Assert.Equal(id, stored.Id);
        Assert.Equal(_w.Id("ana"), stored.AstronautId);
        Assert.Equal("Descansa 24 h", stored.Body);
        Assert.Equal(SyncState.Synced, stored.SyncState);
        Assert.Equal(T(-2), stored.CreatedAt);
    }

    [Fact]
    public async Task Inbound_messages_do_not_appear_in_the_list_of_sent_messages()
    {
        MessageDto inbound = new(Guid.NewGuid(), MessageDirection.ToAstronaut, MessageKind.Message, null, "x", "y", T(-2), T(1), null, null);
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), inbound: new SyncInbound([inbound], [])));

        await _w.Engine.SyncNowAsync();

        Assert.Empty(_w.World.Store.GetMessages(_w.Id("ana")));
        Assert.Equal(0, _w.Engine.PendingCount);
    }

    [Fact]
    public async Task Acknowledged_alerts_are_counted_in_the_report()
    {
        AcknowledgedAlert alert = new(Guid.NewGuid(), T(1), "Dr. Okafor", "Descansa");
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), inbound: new SyncInbound([], [alert])));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(1, result.Report!.AcknowledgedAlerts);
    }

    // ================= Perfil =================

    [Fact]
    public async Task A_pending_profile_edit_goes_in_the_first_call_with_its_updated_at()
    {
        _w.World.ProfileStore.Save(_w.Id("ana"), "Ana R.", "Cometa", "nota");
        UpdateProfileRequest expected = _w.World.ProfileStore.GetPendingEdit(_w.Id("ana"))!;
        AstronautProfile applied = TestWorld.ServerProfile("ana", expected.UpdatedAt) with { DisplayName = "Ana R.", CallSign = "Cometa", Notes = "nota" };
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), profile: applied));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(expected, _w.Api.Syncs.Single().Profile);
        Assert.True(result.Report!.ProfileSynced);
        Assert.Null(_w.World.ProfileStore.GetPendingEdit(_w.Id("ana")));
        Assert.Equal("Ana R.", _w.World.ProfileStore.Get(_w.Id("ana"))!.Profile.DisplayName);
        Assert.Equal(0, _w.Engine.PendingCount);
    }

    [Fact]
    public async Task The_profile_edit_is_sent_only_in_the_first_batch()
    {
        using SyncTestWorld w = new(batchSize: 1);
        w.AddCheckUp("ana", T(-3));
        w.AddCheckUp("ana", T(-2));
        w.World.ProfileStore.Save(w.Id("ana"), "Ana R.", null, null);
        w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), profile: req.Profile is null ? null : TestWorld.ServerProfile("ana", req.Profile.UpdatedAt)));

        await w.Engine.SyncNowAsync();

        Assert.NotNull(w.Api.Syncs[0].Profile);
        Assert.All(w.Api.Syncs.Skip(1), s => Assert.Null(s.Profile));
    }

    [Fact]
    public async Task When_the_server_does_not_apply_the_edit_its_copy_is_downloaded_so_the_edit_does_not_stay_pending_forever()
    {
        _w.World.ProfileStore.Save(_w.Id("ana"), "Ana R.", null, null);
        // El servidor tiene un perfil igual o más nuevo: no aplica la edición y no devuelve el perfil.
        AstronautProfile serverCopy = TestWorld.ServerProfile("ana", T(30)) with { DisplayName = "Cambiada en tierra" };
        _w.Api.OnGetMe = () => serverCopy;

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(1, _w.Api.GetMeCalls);
        Assert.True(result.Report!.ProfileSynced);
        Assert.Null(_w.World.ProfileStore.GetPendingEdit(_w.Id("ana")));
        Assert.Equal("Cambiada en tierra", _w.World.ProfileStore.Get(_w.Id("ana"))!.Profile.DisplayName);
    }

    [Fact]
    public async Task If_the_server_copy_cannot_be_downloaded_the_edit_stays_pending_for_the_next_cycle()
    {
        _w.World.ProfileStore.Save(_w.Id("ana"), "Ana R.", null, null);
        _w.Api.OnGetMe = () => throw new HttpRequestException("caído");

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.False(result.Report!.ProfileSynced);
        Assert.NotNull(_w.World.ProfileStore.GetPendingEdit(_w.Id("ana")));
    }

    [Fact]
    public async Task An_edit_made_while_the_sync_was_in_flight_stays_pending()
    {
        _w.World.ProfileStore.Save(_w.Id("ana"), "Primera", null, null);
        UpdateProfileRequest sent = _w.World.ProfileStore.GetPendingEdit(_w.Id("ana"))!;
        _w.Api.OnSyncAsync = req =>
        {
            // El astronauta vuelve a editar mientras la llamada está en camino.
            _w.World.Clock.Now = T(10);
            _w.World.ProfileStore.Save(_w.Id("ana"), "Segunda", null, null);
            return Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(11), profile: TestWorld.ServerProfile("ana", sent.UpdatedAt) with { DisplayName = "Primera" }));
        };

        await _w.Engine.SyncNowAsync();

        UpdateProfileRequest pending = _w.World.ProfileStore.GetPendingEdit(_w.Id("ana"))!;
        Assert.Equal("Segunda", pending.DisplayName);
        Assert.True(pending.UpdatedAt > sent.UpdatedAt);
    }

    [Fact]
    public async Task A_newer_profile_from_the_server_without_a_local_edit_replaces_the_saved_one()
    {
        AstronautProfile newer = TestWorld.ServerProfile("ana", T(20)) with { DisplayName = "Nombre nuevo" };
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(21), profile: newer));

        await _w.Engine.SyncNowAsync();

        Assert.Equal("Nombre nuevo", _w.World.ProfileStore.Get(_w.Id("ana"))!.Profile.DisplayName);
    }

    // ================= Rangos =================

    private static string RangesJson(string version) =>
        ReferenceRanges.Json.Replace(ReferenceRanges.Version, version);

    [Fact]
    public async Task When_the_server_has_new_ranges_they_are_downloaded_validated_and_cached()
    {
        string newer = RangesJson("2099.01.01-1");
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), rangesUpdate: true));
        _w.Api.OnGetRanges = () => newer;

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.True(result.Report!.RangesDownloaded);
        RangesCache cache = _w.World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().RangesCache.Single();
        Assert.Equal("2099.01.01-1", cache.Version);
        Assert.Equal(newer, cache.Json);
        Assert.Equal("2099.01.01-1", _w.Ranges.LatestVersion);
        Assert.Equal(ReferenceRanges.Version, _w.Ranges.InUseVersion);
        Assert.True(_w.Ranges.RestartPending);
    }

    [Fact]
    public async Task Ranges_are_not_downloaded_unless_the_server_says_there_is_an_update()
    {
        await _w.Engine.SyncNowAsync();

        Assert.Equal(0, _w.Api.GetRangesCalls);
    }

    [Fact]
    public async Task After_downloading_the_next_request_reports_the_new_version_and_they_are_not_downloaded_again()
    {
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), rangesUpdate: req.RangesVersion != "2099.01.01-1"));
        _w.Api.OnGetRanges = () => RangesJson("2099.01.01-1");

        await _w.Engine.SyncNowAsync();
        await _w.Engine.SyncNowAsync();

        Assert.Equal(ReferenceRanges.Version, _w.Api.Syncs[0].RangesVersion);
        Assert.Equal("2099.01.01-1", _w.Api.Syncs[1].RangesVersion);
        Assert.Equal(1, _w.Api.GetRangesCalls);
    }

    [Fact]
    public async Task Invalid_ranges_are_not_saved_and_do_not_break_the_sync()
    {
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), rangesUpdate: true));
        _w.Api.OnGetRanges = () => "{ \"version\": \"9\", esto no es json";
        _w.AddCheckUp("ana", T(-1));

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.False(result.Report!.RangesDownloaded);
        Assert.Empty(_w.World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().RangesCache.ToList());
        Assert.Equal(ReferenceRanges.Version, _w.Ranges.LatestVersion);
        Assert.Equal(0, _w.World.Store.GetPendingCount(_w.Id("ana")));
    }

    [Fact]
    public async Task Ranges_with_the_version_already_held_are_not_saved_again()
    {
        _w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1), rangesUpdate: true));
        _w.Api.OnGetRanges = () => ReferenceRanges.Json;

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.False(result.Report!.RangesDownloaded);
        Assert.Empty(_w.World.Services.CreateScope().ServiceProvider.GetRequiredService<LocalDbContext>().RangesCache.ToList());
    }

    // ================= Cuándo NO se sincroniza, y fallos =================

    [Fact]
    public async Task Without_a_session_nothing_happens()
    {
        using SyncTestWorld w = new(user: null);
        w.World.Session.SignOut();

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Skipped, result.Outcome);
        Assert.Empty(w.Api.Syncs);
    }

    [Fact]
    public async Task Offline_nothing_is_sent_and_everything_stays_pending()
    {
        using SyncTestWorld w = new(online: false);
        w.AddCheckUp("ana", T(-1));

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        Assert.Empty(w.Api.Syncs);
        Assert.Equal(1, w.Engine.PendingCount);
        Assert.Null(w.Engine.StatusText);
    }

    [Fact]
    public async Task Without_tokens_it_asks_the_astronaut_to_sign_in_again_online()
    {
        using SyncTestWorld w = new(withTokens: false);
        w.AddCheckUp("ana", T(-1));

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.NeedsSignIn, result.Outcome);
        Assert.Empty(w.Api.Syncs);
        Assert.Contains("iniciar sesión", w.Engine.StatusText);
        Assert.Equal(1, w.Engine.PendingCount);
    }

    [Fact]
    public async Task A_401_after_the_client_tried_to_refresh_means_the_session_expired()
    {
        _w.AddCheckUp("ana", T(-1));
        _w.Api.OnSyncAsync = _ => throw ScriptedCrewApiClient.Status(HttpStatusCode.Unauthorized);

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.NeedsSignIn, result.Outcome);
        Assert.Contains("caducó", _w.Engine.StatusText);
        Assert.Equal(1, _w.Engine.PendingCount);
    }

    [Fact]
    public async Task A_network_failure_keeps_everything_pending_and_the_next_attempt_uploads_it()
    {
        _w.AddCheckUp("ana", T(-3));
        _w.AddCheckUp("ana", T(-2));
        _w.Api.OnSyncAsync = _ => throw new HttpRequestException("connection refused");

        SyncResult failed = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.ServerUnreachable, failed.Outcome);
        Assert.Contains("Sin respuesta", _w.Engine.StatusText);
        Assert.Equal(2, _w.Engine.PendingCount);

        _w.Api.OnSyncAsync = null;
        SyncResult ok = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Completed, ok.Outcome);
        Assert.Equal(0, _w.Engine.PendingCount);
        Assert.Null(_w.Engine.StatusText);
    }

    [Fact]
    public async Task A_timeout_counts_as_the_server_not_answering()
    {
        _w.Api.OnSyncAsync = _ => throw new TaskCanceledException("timeout", new TimeoutException());

        Assert.Equal(SyncOutcome.ServerUnreachable, (await _w.Engine.SyncNowAsync()).Outcome);
    }

    [Fact]
    public async Task Any_other_failure_is_reported_and_keeps_what_is_pending()
    {
        _w.AddCheckUp("ana", T(-1));
        _w.Api.OnSyncAsync = _ => throw new InvalidOperationException("respuesta vacía");

        SyncResult result = await _w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Contains("respuesta vacía", _w.Engine.StatusText);
        Assert.Equal(1, _w.Engine.PendingCount);
    }

    [Fact]
    public async Task A_failure_in_the_second_batch_keeps_the_first_one_synced()
    {
        using SyncTestWorld w = new(batchSize: 1);
        CheckUpRecord first = w.AddCheckUp("ana", T(-20));
        CheckUpRecord second = w.AddCheckUp("ana", T(-10));
        w.Api.OnSyncAsync = req => req.CheckUps.Any(c => c.Id == second.Id)
            ? throw new HttpRequestException("se cayó")
            : Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1)));

        SyncResult result = await w.Engine.SyncNowAsync();

        Assert.Equal(SyncOutcome.ServerUnreachable, result.Outcome);
        Assert.Equal(SyncState.Synced, w.CheckUps().Single(c => c.Id == first.Id).SyncState);
        Assert.Equal(SyncState.Pending, w.CheckUps().Single(c => c.Id == second.Id).SyncState);
    }

    [Fact]
    public async Task Cancelling_stops_the_sync_without_turning_it_into_an_error()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _w.Engine.SyncNowAsync(cts.Token));
    }

    // ================= Estado para la barra =================

    [Fact]
    public void The_pending_count_adds_check_ups_messages_and_the_profile_edit_of_the_open_session()
    {
        _w.AddCheckUp("ana", T(-1));
        _w.World.Store.QueueMessage(_w.Id("ana"), "hola");
        _w.World.ProfileStore.Save(_w.Id("ana"), "Ana R.", null, null);
        _w.AddCheckUp("luis", T(-1));

        Assert.Equal(3, _w.Engine.PendingCount);
    }

    [Fact]
    public async Task It_reports_syncing_while_the_call_is_in_flight()
    {
        TaskCompletionSource<SyncResponse> gate = new();
        _w.Api.OnSyncAsync = _ => gate.Task;
        List<bool> seen = [];
        _w.Engine.Changed += (_, _) => seen.Add(_w.Engine.IsSyncing);

        Task<SyncResult> running = _w.Engine.SyncNowAsync();
        await WaitUntil(() => _w.Engine.IsSyncing);
        Assert.True(_w.Engine.IsSyncing);

        gate.SetResult(ScriptedCrewApiClient.AcceptAll(new SyncRequest("x", null, "v", null, [], []), T(1)));
        await running;

        Assert.False(_w.Engine.IsSyncing);
        Assert.Contains(true, seen);
        Assert.False(seen[^1]);
    }

    [Fact]
    public async Task Syncs_never_overlap()
    {
        int concurrent = 0;
        int maxConcurrent = 0;
        _w.Api.OnSyncAsync = async req =>
        {
            int now = Interlocked.Increment(ref concurrent);
            maxConcurrent = Math.Max(maxConcurrent, now);
            await Task.Delay(40);
            Interlocked.Decrement(ref concurrent);
            return ScriptedCrewApiClient.AcceptAll(req, T(1));
        };

        await Task.WhenAll(_w.Engine.SyncNowAsync(), _w.Engine.SyncNowAsync(), _w.Engine.SyncNowAsync());

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(3, _w.Api.Syncs.Count);
    }

    [Fact]
    public async Task Every_attempt_raises_completed_with_its_result()
    {
        List<SyncOutcome> outcomes = [];
        _w.Engine.Completed += (_, r) => outcomes.Add(r.Outcome);

        await _w.Engine.SyncNowAsync();
        _w.World.Connectivity.Set(false);
        await _w.Engine.SyncNowAsync();

        Assert.Equal([SyncOutcome.Completed, SyncOutcome.Offline], outcomes);
    }

    [Fact]
    public void Sync_now_only_requests_and_returns_immediately()
    {
        _w.Engine.SyncNow();

        Assert.Empty(_w.Api.Syncs);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < limit)
        {
            await Task.Delay(10);
        }
    }
}
