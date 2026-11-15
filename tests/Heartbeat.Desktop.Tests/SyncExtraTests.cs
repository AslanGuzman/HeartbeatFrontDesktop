using System.Net.Http;

using Heartbeat.Client;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class SyncStatusBarTests
{
    private readonly FakeConnectivityService _connectivity = new();
    private readonly FakeSyncStatusProvider _sync = new();

    private StatusBarViewModel Create() => new(_connectivity, _sync, new ImmediateSynchronizationContext());

    [Fact]
    public void Nothing_extra_is_shown_when_everything_is_fine()
    {
        StatusBarViewModel bar = Create();

        Assert.False(bar.HasSyncState);
        Assert.False(bar.HasRejected);
        Assert.Equal(string.Empty, bar.SyncStateText);
    }

    [Fact]
    public void It_shows_syncing_while_the_engine_runs()
    {
        StatusBarViewModel bar = Create();

        _sync.Set(null, 2, isSyncing: true);

        Assert.True(bar.IsSyncing);
        Assert.Equal("Sincronizando…", bar.SyncStateText);
        Assert.True(bar.HasSyncState);
    }

    [Fact]
    public void It_shows_the_notice_of_the_last_attempt_when_it_is_not_syncing()
    {
        StatusBarViewModel bar = Create();

        _sync.Set(null, 1, statusText: "Sin respuesta del servidor. Se reintentará.");

        Assert.Equal("Sin respuesta del servidor. Se reintentará.", bar.SyncStateText);
        Assert.True(bar.HasSyncState);

        _sync.Set(null, 0, isSyncing: true, statusText: "Sin respuesta del servidor. Se reintentará.");
        Assert.Equal("Sincronizando…", bar.SyncStateText);
    }

    [Theory]
    [InlineData(1, "1 rechazado")]
    [InlineData(3, "3 rechazados")]
    public void Rejected_items_are_counted_in_the_bar(int rejected, string text)
    {
        StatusBarViewModel bar = Create();

        _sync.Set(null, 0, rejected);

        Assert.True(bar.HasRejected);
        Assert.Equal(text, bar.RejectedText);
    }

    [Fact]
    public void The_sync_now_button_asks_the_engine_to_sync()
    {
        StatusBarViewModel bar = Create();

        bar.SyncNowCommand.Execute(null);

        Assert.Equal(1, _sync.SyncRequests);
    }
}

public sealed class DemoServerSyncTests
{
    private static LoginRequest Login(string user = "ana") => new(user, DemoAccounts.Password, "equipo");

    private static SyncRequest Request(UpdateProfileRequest? profile = null, CheckUpDto[]? checkUps = null, MessageDto[]? messages = null) =>
        new("equipo", null, "v", profile, checkUps ?? [], messages ?? []);

    private static CheckUpDto CheckUp(string? note = null) =>
        new(Guid.NewGuid(), TestWorld.Now, null, HealthStatus.Normal, "v", note, null, []);

    private static MessageDto Message(string body) =>
        new(Guid.NewGuid(), MessageDirection.ToMissionControl, MessageKind.Message, null, "s", body, TestWorld.Now, null, null, null);

    [Fact]
    public async Task It_accepts_check_ups_and_messages_and_is_idempotent()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));
        await api.LoginAsync(Login());
        CheckUpDto checkUp = CheckUp();
        MessageDto message = Message("hola");

        SyncResponse first = await api.SyncAsync(Request(checkUps: [checkUp], messages: [message]));
        SyncResponse again = await api.SyncAsync(Request(checkUps: [checkUp], messages: [message]));

        Assert.Equal([checkUp.Id, message.Id], first.Accepted);
        Assert.Equal(first.Accepted, again.Accepted);
        Assert.Empty(first.Rejected);
    }

    [Fact]
    public async Task It_rejects_items_that_contain_the_reject_marker_so_a_rejection_can_be_seen()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));
        await api.LoginAsync(Login());
        CheckUpDto bad = CheckUp("probando [RECHAZAR]");
        MessageDto badMessage = Message("[rechazar] esto");
        CheckUpDto ok = CheckUp("normal");

        SyncResponse response = await api.SyncAsync(Request(checkUps: [bad, ok], messages: [badMessage]));

        Assert.Equal([ok.Id], response.Accepted);
        Assert.Equal([bad.Id, badMessage.Id], response.Rejected.Select(r => r.Id));
        Assert.All(response.Rejected, r => Assert.Contains("demostración", r.Reason));
    }

    [Fact]
    public async Task The_profile_comes_back_only_when_the_edit_was_applied_like_the_real_server()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));
        CrewLoginResponse login = await api.LoginAsync(Login());
        UpdateProfileRequest edit = new("Ana R.", "Cometa", "nota", TestWorld.Now.AddDays(1));

        SyncResponse applied = await api.SyncAsync(Request(edit));
        SyncResponse repeated = await api.SyncAsync(Request(edit));
        SyncResponse none = await api.SyncAsync(Request());

        Assert.Equal("Ana R.", applied.Profile!.DisplayName);
        Assert.Equal(edit.UpdatedAt, applied.Profile.UpdatedAt);
        Assert.Null(repeated.Profile);
        Assert.Null(none.Profile);
        Assert.Equal("Ana R.", (await api.GetMeAsync()).DisplayName);
        Assert.NotEqual(login.Profile.DisplayName, (await api.GetMeAsync()).DisplayName);
    }

    [Fact]
    public async Task An_older_edit_is_not_applied()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));
        await api.LoginAsync(Login());

        SyncResponse response = await api.SyncAsync(Request(new UpdateProfileRequest("Vieja", null, null, TestWorld.Now.AddDays(-365))));

        Assert.Null(response.Profile);
        Assert.NotEqual("Vieja", (await api.GetMeAsync()).DisplayName);
    }

    [Fact]
    public async Task The_server_remembers_the_edit_when_the_astronaut_logs_in_again()
    {
        using DemoCrewApiClient api = new(new FixedTimeProvider(TestWorld.Now));
        await api.LoginAsync(Login());
        await api.SyncAsync(Request(new UpdateProfileRequest("Ana R.", null, null, TestWorld.Now.AddDays(1))));

        CrewLoginResponse again = await api.LoginAsync(Login());

        Assert.Equal("Ana R.", again.Profile.DisplayName);
    }
}

public sealed class RejectedDisplayTests
{
    [Fact]
    public async Task A_rejected_check_up_shows_its_reason_in_the_history()
    {
        using SyncTestWorld w = new();
        CheckUpRecord bad = w.AddCheckUp("ana", TestWorld.Now.AddMinutes(-2));
        w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(TestWorld.Now, [], [new SyncRejected(bad.Id, "Reading value outside valid range")], null, new SyncInbound([], []), false));
        await w.Engine.SyncNowAsync();

        HistoryViewModel history = new(w.World.Store, w.World.Session, w.World.Info);

        CheckUpRowViewModel row = history.CheckUps.First();
        Assert.Equal("Rechazado: Reading value outside valid range", row.SyncText);
        Assert.False(row.IsSynced);
    }

    [Fact]
    public async Task A_rejected_message_shows_its_reason_in_the_messages_list()
    {
        using SyncTestWorld w = new();
        MessageRecord message = w.World.Store.QueueMessage(w.Id("ana"), "hola");
        w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(TestWorld.Now, [], [new SyncRejected(message.Id, "Message body is too long")], null, new SyncInbound([], []), false));
        await w.Engine.SyncNowAsync();

        MessagesViewModel messages = new(w.World.Store, w.World.Session);

        Assert.Equal("Rechazado: Message body is too long", messages.Messages.Single().StatusText);
        Assert.False(messages.Messages.Single().IsSent);
    }

    [Fact]
    public async Task Synced_items_say_so()
    {
        using SyncTestWorld w = new();
        w.AddCheckUp("ana", TestWorld.Now.AddMinutes(-2));
        await w.Engine.SyncNowAsync();

        HistoryViewModel history = new(w.World.Store, w.World.Session, w.World.Info);

        Assert.Equal("Sincronizado", history.CheckUps.First().SyncText);
    }

    [Fact]
    public async Task Open_screens_refresh_when_the_engine_marks_items_as_sent()
    {
        using SyncTestWorld w = new();
        w.AddCheckUp("ana", TestWorld.Now.AddMinutes(-2));
        w.World.Store.QueueMessage(w.Id("ana"), "hola");
        HistoryViewModel history = new(w.World.Store, w.World.Session, w.World.Info);
        MessagesViewModel messages = new(w.World.Store, w.World.Session);
        Assert.Equal("Pendiente de sincronizar", history.CheckUps.First().SyncText);
        Assert.False(messages.Messages.Single().IsSent);

        await w.Engine.SyncNowAsync();

        Assert.Equal("Sincronizado", history.CheckUps.First().SyncText);
        Assert.True(messages.Messages.Single().IsSent);
    }

    [Fact]
    public async Task Open_screens_refresh_when_the_engine_marks_items_as_rejected()
    {
        using SyncTestWorld w = new();
        MessageRecord message = w.World.Store.QueueMessage(w.Id("ana"), "hola");
        MessagesViewModel messages = new(w.World.Store, w.World.Session);
        w.Api.OnSyncAsync = req => Task.FromResult(new SyncResponse(TestWorld.Now, [], [new SyncRejected(message.Id, "no")], null, new SyncInbound([], []), false));

        await w.Engine.SyncNowAsync();

        Assert.Equal("Rechazado: no", messages.Messages.Single().StatusText);
    }
}
