using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Sync;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Redactar, estados por mensaje y bandeja de respuestas y alertas (MR-11, #33).</summary>
public sealed class MessagesViewModelTests
{
    private static DateTimeOffset T(int minutes) => TestWorld.Now.AddMinutes(minutes);

    private static MessageDto Reply(string subject, string body, int minutesAgo, string? sender = "Dr. Okafor", DateTimeOffset? readAt = null) =>
        new(Guid.CreateVersion7(), MessageDirection.ToAstronaut, MessageKind.Message, null, subject, body, T(-minutesAgo), null, readAt, sender);

    private static Task<SyncResult> PullAsync(SyncTestWorld w, IReadOnlyList<MessageDto> messages, IReadOnlyList<AcknowledgedAlert>? alerts = null)
    {
        w.Api.OnSyncAsync = req => Task.FromResult(ScriptedCrewApiClient.AcceptAll(req, T(1)) with
        {
            Inbound = new SyncInbound(messages, alerts ?? []),
        });
        return w.Engine.SyncNowAsync();
    }

    private static MessagesViewModel NewVm(SyncTestWorld w) => new(w.World.Store, w.World.Session, w.World.Info);

    // ---------------- Redactar ----------------

    [Fact]
    public void Sending_is_disabled_until_there_is_text()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);

        Assert.False(vm.SendCommand.CanExecute(null));

        vm.NewMessage = "   ";
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.NewMessage = "Hola";
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void A_report_also_needs_a_check_up()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.IsReportMode = true;
        vm.NewMessage = "Mira esto";

        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.NotEmpty(vm.CheckUps);

        vm.SelectedCheckUp = vm.CheckUps[0];

        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Offline_the_message_waits_in_the_queue_and_goes_out_when_the_link_returns()
    {
        TestWorld world = new(online: false);
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.NewMessage = "Necesito revisar el sueño";

        vm.SendCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewMessage);
        OutgoingRowViewModel row = Assert.Single(vm.Sent);
        Assert.Equal(MessageStatus.Queued, row.Status);
        Assert.Equal("En cola (sin enlace)", row.StatusText);
        Assert.Equal(1, world.Store.PendingCount);
        Assert.True(vm.IsSentTab);

        world.Connectivity.Set(true);

        Assert.Equal(MessageStatus.Sent, vm.Sent[0].Status);
        Assert.StartsWith("Enviado", vm.Sent[0].StatusText);
        Assert.Equal(0, world.Store.PendingCount);
    }

    [Fact]
    public void A_message_uses_the_subject_typed_or_the_first_line()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);

        vm.Subject = "Sueño";
        vm.NewMessage = "Duermo poco\nDesde el lunes";
        vm.SendCommand.Execute(null);
        world.Clock.Now = world.Clock.Now.AddMinutes(1);
        vm.NewMessage = "Sin asunto escrito\nsegunda línea";
        vm.SendCommand.Execute(null);

        Assert.Equal(["Sin asunto escrito", "Sueño"], vm.Sent.Select(r => r.Title));
    }

    [Fact]
    public void Sending_a_report_attaches_the_chosen_check_up_and_fills_in_the_text()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.IsReportMode = true;
        CheckUpOptionViewModel option = vm.CheckUps[0];

        vm.SelectedCheckUp = option;

        Assert.Equal(ReportBuilder.Subject(option.CheckUp), vm.Subject);
        Assert.Equal(ReportBuilder.Body(option.CheckUp, world.Info), vm.NewMessage);
        Assert.StartsWith("Estado general:", vm.NewMessage);

        vm.SendCommand.Execute(null);

        MessageRecord stored = world.Store.GetMessages(TestWorld.IdOf("ana")).Single();
        Assert.True(stored.IsReport);
        Assert.Equal(option.CheckUp.Id, stored.CheckUpId);
        OutgoingRowViewModel row = Assert.Single(vm.Sent);
        Assert.True(row.IsReport);
        Assert.StartsWith("Reporte de check-up:", row.Title);
        Assert.Null(vm.SelectedCheckUp);
    }

    [Fact]
    public void A_report_text_the_user_edited_is_not_overwritten_and_a_check_up_cannot_be_reported_twice()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.IsReportMode = true;
        Guid checkUpId = vm.CheckUps[0].CheckUp.Id;
        vm.NewMessage = "Mi propio texto";

        vm.SelectedCheckUp = vm.CheckUps[0];

        Assert.Equal("Mi propio texto", vm.NewMessage);

        vm.SendCommand.Execute(null);

        CheckUpOptionViewModel again = vm.CheckUps.Single(c => c.CheckUp.Id == checkUpId);
        Assert.True(again.AlreadyReported);
        Assert.Contains("ya reportado", again.Label);
        vm.NewMessage = "otra vez";
        vm.SelectedCheckUp = again;
        Assert.True(vm.SelectedAlreadyReported);
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.SendCommand.Execute(null);

        Assert.Single(world.Store.GetMessages(TestWorld.IdOf("ana")));
    }

    [Fact]
    public void A_text_over_the_limit_shows_the_error_and_keeps_what_was_typed()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.NewMessage = new string('x', LocalHealthRecordStore.MaxMessageLength + 1);

        vm.SendCommand.Execute(null);

        Assert.NotEqual(string.Empty, vm.Error);
        Assert.Empty(vm.Sent);
        Assert.NotEqual(string.Empty, vm.NewMessage);
    }

    [Fact]
    public void Sent_messages_are_listed_newest_first_and_only_for_the_signed_in_astronaut()
    {
        TestWorld world = new("ana");
        world.Store.QueueMessage(TestWorld.IdOf("luis"), "de luis");
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.NewMessage = "primero";
        vm.SendCommand.Execute(null);
        world.Clock.Now = world.Clock.Now.AddMinutes(1);
        vm.NewMessage = "segundo";
        vm.SendCommand.Execute(null);

        Assert.Equal(["segundo", "primero"], vm.Sent.Select(m => m.Body));
        Assert.True(vm.HasSent);
    }

    [Fact]
    public void A_message_control_has_read_shows_as_read()
    {
        TestWorld world = new();
        Guid ana = TestWorld.IdOf("ana");
        MessageRecord message = world.Store.QueueMessage(ana, "hola");
        world.Store.MarkAllSynced(T(1));
        using (IServiceScope scope = world.Services.CreateScope())
        {
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.Messages.Single(m => m.Id == message.Id).ReadAt = T(5);
            db.SaveChanges();
        }

        MessagesViewModel vm = new(world.Store, world.Session, world.Info);

        OutgoingRowViewModel row = Assert.Single(vm.Sent);
        Assert.Equal(MessageStatus.Read, row.Status);
        Assert.StartsWith("Leído", row.StatusText);
    }

    [Fact]
    public void The_mode_and_tab_buttons_switch_what_is_shown()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        Assert.True(vm.IsMessageMode);
        Assert.True(vm.IsInboxTab);

        vm.ComposeReportCommand.Execute(null);
        vm.ShowSentCommand.Execute(null);

        Assert.True(vm.IsReportMode);
        Assert.False(vm.IsMessageMode);
        Assert.True(vm.IsSentTab);

        vm.ComposeMessageCommand.Execute(null);
        vm.ShowInboxCommand.Execute(null);

        Assert.True(vm.IsMessageMode);
        Assert.True(vm.IsInboxTab);
    }

    [Fact]
    public void A_one_line_message_does_not_repeat_itself_but_a_longer_one_shows_its_text()
    {
        TestWorld world = new();
        MessagesViewModel vm = new(world.Store, world.Session, world.Info);
        vm.NewMessage = "Solo una línea";
        vm.SendCommand.Execute(null);
        world.Clock.Now = world.Clock.Now.AddMinutes(1);
        vm.NewMessage = "Primera\nSegunda";
        vm.SendCommand.Execute(null);

        Assert.True(vm.Sent[0].ShowBody);
        Assert.False(vm.Sent[1].ShowBody);
    }

    // ---------------- Bandeja ----------------

    [Fact]
    public async Task Replies_that_arrive_by_sync_land_in_the_inbox_unread_with_who_wrote_them()
    {
        using SyncTestWorld w = new();
        MessagesViewModel vm = NewVm(w);
        Assert.False(vm.HasInbox);

        await PullAsync(w, [Reply("Descansa", "Descansa 24 h", 10)]);

        InboxRowViewModel row = Assert.Single(vm.Inbox);
        Assert.Equal("Descansa", row.Title);
        Assert.Equal("Descansa 24 h", row.Body);
        Assert.Equal("Dr. Okafor", row.From);
        Assert.True(row.IsUnread);
        Assert.False(row.IsAlert);
        Assert.Equal(1, vm.UnreadCount);
        Assert.Equal("Bandeja (1)", vm.InboxTabText);
    }

    [Fact]
    public async Task Marking_a_reply_as_read_updates_the_count_and_the_row()
    {
        using SyncTestWorld w = new();
        MessagesViewModel vm = NewVm(w);
        MessageDto reply = Reply("Hola", "cuerpo", 3);
        await PullAsync(w, [reply, Reply("Otra", "cuerpo", 20)]);

        vm.MarkReadCommand.Execute(reply.Id);

        Assert.Equal(1, vm.UnreadCount);
        Assert.False(vm.Inbox.Single(r => r.MessageId == reply.Id).IsUnread);

        vm.MarkAllReadCommand.Execute(null);

        Assert.Equal(0, vm.UnreadCount);
        Assert.Equal("Bandeja", vm.InboxTabText);
        Assert.All(vm.Inbox, r => Assert.False(r.IsUnread));
    }

    [Fact]
    public async Task Acknowledged_alerts_appear_in_the_inbox_with_who_acknowledged_them()
    {
        using SyncTestWorld w = new();
        CheckUpRecord critical = w.AddCheckUp("ana", T(-30), heartRate: 150);
        await w.Engine.SyncNowAsync();
        MessagesViewModel vm = NewVm(w);

        await PullAsync(w, [], [new AcknowledgedAlert(critical.Id, T(-5), "Dr. Okafor", "Ya hablé con tu oficial médico")]);

        InboxRowViewModel row = Assert.Single(vm.Inbox);
        Assert.True(row.IsAlert);
        Assert.Equal("Alerta reconocida", row.Title);
        Assert.Contains("Dr. Okafor", row.Body);
        Assert.Contains("Ya hablé con tu oficial médico", row.Body);
        Assert.False(row.IsUnread);
        Assert.Equal(0, vm.UnreadCount);
    }

    [Fact]
    public async Task The_inbox_mixes_replies_and_alerts_newest_first()
    {
        using SyncTestWorld w = new();
        CheckUpRecord critical = w.AddCheckUp("ana", T(-60), heartRate: 150);
        MessagesViewModel vm = NewVm(w);

        await PullAsync(w, [Reply("Vieja", "a", 40), Reply("Nueva", "b", 2)], [new AcknowledgedAlert(critical.Id, T(-20), "Dr. Okafor", null)]);

        Assert.Equal(["Nueva", "Alerta reconocida", "Vieja"], vm.Inbox.Select(r => r.Title));
    }

    // ---------------- Almacén y motor ----------------

    [Fact]
    public async Task Receiving_the_same_reply_or_alert_twice_does_not_duplicate_it()
    {
        using SyncTestWorld w = new();
        CheckUpRecord critical = w.AddCheckUp("ana", T(-30), heartRate: 150);
        MessageDto reply = Reply("Hola", "cuerpo", 4);
        AcknowledgedAlert alert = new(critical.Id, T(-3), "Dr. Okafor", null);

        await PullAsync(w, [reply], [alert]);
        await PullAsync(w, [reply], [alert with { By = "Dra. Lind", Note = "Seguimiento" }]);

        Assert.Single(w.World.Store.GetInbox(w.Id("ana")));
        AlertAcknowledgementRecord stored = Assert.Single(w.World.Store.GetAcknowledgedAlerts(w.Id("ana")));
        Assert.Equal("Dra. Lind", stored.AcknowledgedBy);
        Assert.Equal("Seguimiento", stored.Note);
    }

    [Fact]
    public async Task An_alert_for_a_check_up_that_is_not_the_users_is_ignored()
    {
        using SyncTestWorld w = new();
        CheckUpRecord others = w.AddCheckUp("luis", T(-30), heartRate: 150);

        await PullAsync(w, [], [new AcknowledgedAlert(others.Id, T(-3), "Dr. Okafor", null)]);

        Assert.Empty(w.World.Store.GetAcknowledgedAlerts(w.Id("ana")));
        Assert.Empty(w.World.Store.GetAcknowledgedAlerts(w.Id("luis")));
    }

    [Fact]
    public async Task Open_screens_refresh_when_only_an_alert_arrives()
    {
        using SyncTestWorld w = new();
        CheckUpRecord critical = w.AddCheckUp("ana", T(-30), heartRate: 150);
        int changes = 0;
        w.World.Store.Changed += (_, _) => changes++;

        await PullAsync(w, [], [new AcknowledgedAlert(critical.Id, T(-3), "Dr. Okafor", null)]);

        Assert.True(changes >= 1);
    }

    [Fact]
    public async Task Replies_belong_to_their_astronaut_and_only_they_can_mark_them_read()
    {
        using SyncTestWorld w = new();
        MessageDto reply = Reply("Hola", "cuerpo", 4);
        await PullAsync(w, [reply]);
        IHealthRecordStore store = w.World.Store;

        Assert.False(store.MarkRead(w.Id("luis"), reply.Id));
        Assert.False(store.MarkRead(w.Id("ana"), Guid.NewGuid()));
        Assert.Equal(1, store.GetUnreadCount(w.Id("ana")));
        Assert.True(store.MarkRead(w.Id("ana"), reply.Id));
        Assert.False(store.MarkRead(w.Id("ana"), reply.Id));
        Assert.Equal(0, store.GetUnreadCount(w.Id("ana")));
        Assert.Equal(TestWorld.Now, store.GetInbox(w.Id("ana")).Single().ReadAt);
    }

    [Fact]
    public async Task A_reply_the_server_already_marked_read_arrives_read()
    {
        using SyncTestWorld w = new();

        await PullAsync(w, [Reply("Hola", "cuerpo", 4, readAt: T(-2))]);

        MessageRecord stored = w.World.Store.GetInbox(w.Id("ana")).Single();
        Assert.Equal(MessageStatus.Opened, stored.Status);
        Assert.Equal(0, w.World.Store.GetUnreadCount(w.Id("ana")));
    }

    [Fact]
    public async Task Inbound_replies_are_not_counted_as_pending_or_listed_as_sent()
    {
        using SyncTestWorld w = new();

        await PullAsync(w, [Reply("Hola", "cuerpo", 4)]);

        Assert.Equal(0, w.Engine.PendingCount);
        Assert.Empty(w.World.Store.GetMessages(w.Id("ana")));
    }
}
