using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class ProfileCacheTests
{
    private static readonly AstronautProfile Server = TestWorld.ServerProfile("ana");
    private static readonly UpdateProfileRequest Edit = new("Ana R.", "Cometa", "nota", TestWorld.Now);

    [Fact]
    public void A_profile_with_a_pending_edit_round_trips()
    {
        CachedProfile read = ProfileCache.Read(ProfileCache.Write(new CachedProfile(Server, Edit)))!;

        Assert.Equal(Server, read.Profile);
        Assert.Equal(Edit, read.PendingEdit);
    }

    [Fact]
    public void A_profile_without_edit_round_trips()
    {
        CachedProfile read = ProfileCache.Read(ProfileCache.Write(new CachedProfile(Server, null)))!;

        Assert.Equal(Server, read.Profile);
        Assert.Null(read.PendingEdit);
    }

    [Fact]
    public void The_format_used_before_pending_edits_existed_is_still_read()
    {
        string legacy = System.Text.Json.JsonSerializer.Serialize(Server, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        CachedProfile read = ProfileCache.Read(legacy)!;

        Assert.Equal(Server, read.Profile);
        Assert.Null(read.PendingEdit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"profile\":null}")]
    public void Missing_or_damaged_json_reads_as_no_profile(string? json)
    {
        Assert.Null(ProfileCache.Read(json));
    }

    [Fact]
    public void The_effective_profile_applies_the_pending_edit_and_keeps_the_rest()
    {
        AstronautProfile effective = new CachedProfile(Server, Edit).Effective;

        Assert.Equal("Ana R.", effective.DisplayName);
        Assert.Equal("Cometa", effective.CallSign);
        Assert.Equal("nota", effective.Notes);
        Assert.Equal(Edit.UpdatedAt, effective.UpdatedAt);
        Assert.Equal(Server.UserName, effective.UserName);
        Assert.Equal(Server.Mission, effective.Mission);
        Assert.Equal(Server.DateOfBirth, effective.DateOfBirth);
        Assert.Same(Server, new CachedProfile(Server, null).Effective);
    }

    [Fact]
    public void A_newer_pending_edit_survives_a_fresh_server_profile()
    {
        string json = ProfileCache.Write(new CachedProfile(Server, Edit));
        AstronautProfile olderServer = Server with { UpdatedAt = Edit.UpdatedAt.AddHours(-1), DisplayName = "Del servidor" };

        CachedProfile merged = ProfileCache.MergeServerProfile(json, olderServer);

        Assert.Equal(Edit, merged.PendingEdit);
        Assert.Equal("Ana R.", merged.Effective.DisplayName);
        Assert.Equal("Del servidor", merged.Profile.DisplayName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_pending_edit_that_is_not_newer_than_the_server_profile_is_dropped(int hoursNewerOnServer)
    {
        string json = ProfileCache.Write(new CachedProfile(Server, Edit));
        AstronautProfile server = Server with { UpdatedAt = Edit.UpdatedAt.AddHours(hoursNewerOnServer), DisplayName = "Del servidor" };

        CachedProfile merged = ProfileCache.MergeServerProfile(json, server);

        Assert.Null(merged.PendingEdit);
        Assert.Equal("Del servidor", merged.Effective.DisplayName);
    }

    [Fact]
    public void Without_a_saved_profile_the_server_one_is_used_as_is()
    {
        CachedProfile merged = ProfileCache.MergeServerProfile(null, Server);

        Assert.Equal(Server, merged.Profile);
        Assert.Null(merged.PendingEdit);
    }
}

public sealed class LocalProfileStoreTests
{
    private readonly TestWorld _world = new("ana", online: false);
    private readonly Guid _ana = TestWorld.IdOf("ana");

    [Fact]
    public void The_saved_profile_comes_back_without_pending_edits()
    {
        ProfileView view = _world.ProfileStore.Get(_ana)!;

        Assert.False(view.HasPendingEdit);
        Assert.Equal("Ana Rivera", view.Profile.DisplayName);
        Assert.Equal(0, _world.ProfileStore.PendingCount);
    }

    [Fact]
    public void Saving_stores_a_pending_edit_stamped_with_now_and_shows_it_right_away()
    {
        ProfileView saved = _world.ProfileStore.Save(_ana, "Ana R.", "Cometa", "Sin alergias");

        Assert.True(saved.HasPendingEdit);
        Assert.Equal("Ana R.", saved.Profile.DisplayName);
        Assert.Equal(TestWorld.Now, saved.Profile.UpdatedAt);
        Assert.Equal("Ana R.", _world.ProfileStore.Get(_ana)!.Profile.DisplayName);
        Assert.Equal(1, _world.ProfileStore.PendingCount);
    }

    [Fact]
    public void The_pending_edit_is_what_the_next_sync_will_send_with_its_updated_at()
    {
        _world.ProfileStore.Save(_ana, "Ana R.", "Cometa", "Sin alergias");

        UpdateProfileRequest pending = _world.ProfileStore.GetPendingEdit(_ana)!;

        Assert.Equal(new UpdateProfileRequest("Ana R.", "Cometa", "Sin alergias", TestWorld.Now), pending);
    }

    [Fact]
    public void Only_the_three_editable_fields_change()
    {
        AstronautProfile before = _world.ProfileStore.Get(_ana)!.Profile;

        AstronautProfile after = _world.ProfileStore.Save(_ana, "Otro", null, null).Profile;

        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.UserName, after.UserName);
        Assert.Equal(before.Mission, after.Mission);
        Assert.Equal(before.DateOfBirth, after.DateOfBirth);
    }

    [Fact]
    public void Text_is_trimmed_and_blank_optional_fields_become_null()
    {
        _world.ProfileStore.Save(_ana, "  Ana R.  ", "   ", "");

        UpdateProfileRequest pending = _world.ProfileStore.GetPendingEdit(_ana)!;

        Assert.Equal("Ana R.", pending.DisplayName);
        Assert.Null(pending.CallSign);
        Assert.Null(pending.Notes);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    public void The_name_is_required(string name, string? callSign, string? notes)
    {
        Assert.Throws<ArgumentException>(() => _world.ProfileStore.Save(_ana, name, callSign, notes));
        Assert.Null(_world.ProfileStore.GetPendingEdit(_ana));
    }

    [Fact]
    public void The_contract_limits_are_enforced_and_exactly_the_limit_is_fine()
    {
        Assert.Throws<ArgumentException>(() => _world.ProfileStore.Save(_ana, new string('a', 101), null, null));
        Assert.Throws<ArgumentException>(() => _world.ProfileStore.Save(_ana, "Ana", new string('a', 31), null));
        Assert.Throws<ArgumentException>(() => _world.ProfileStore.Save(_ana, "Ana", null, new string('a', 501)));
        Assert.Null(_world.ProfileStore.GetPendingEdit(_ana));

        _world.ProfileStore.Save(_ana, new string('a', 100), new string('b', 30), new string('c', 500));

        Assert.NotNull(_world.ProfileStore.GetPendingEdit(_ana));
    }

    [Fact]
    public void A_second_edit_replaces_the_pending_one_and_never_goes_back_in_time()
    {
        _world.ProfileStore.Save(_ana, "Primera", null, null);
        DateTimeOffset first = _world.ProfileStore.GetPendingEdit(_ana)!.UpdatedAt;

        // El reloj del equipo va atrasado respecto a la edición anterior.
        _world.Clock.Now = TestWorld.Now.AddMinutes(-5);
        _world.ProfileStore.Save(_ana, "Segunda", null, null);

        UpdateProfileRequest pending = _world.ProfileStore.GetPendingEdit(_ana)!;
        Assert.Equal("Segunda", pending.DisplayName);
        Assert.True(pending.UpdatedAt > first);
        Assert.Equal(1, _world.ProfileStore.PendingCount);
    }

    [Fact]
    public void Each_astronaut_has_their_own_profile_and_pending_edit()
    {
        Guid luis = TestWorld.IdOf("luis");

        _world.ProfileStore.Save(_ana, "Ana R.", null, null);

        Assert.Equal("Luis Ortega", _world.ProfileStore.Get(luis)!.Profile.DisplayName);
        Assert.Null(_world.ProfileStore.GetPendingEdit(luis));
        Assert.Equal(1, _world.ProfileStore.PendingCount);
    }

    [Fact]
    public void Saving_for_an_astronaut_without_a_local_account_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => _world.ProfileStore.Save(Guid.NewGuid(), "Nadie", null, null));
        Assert.Null(_world.ProfileStore.Get(Guid.NewGuid()));
    }

    [Fact]
    public void When_the_server_accepts_the_edit_it_becomes_the_reference_profile()
    {
        ProfileView saved = _world.ProfileStore.Save(_ana, "Ana R.", "Cometa", null);

        _world.ProfileStore.MarkSynced(_ana, saved.Profile);

        ProfileView after = _world.ProfileStore.Get(_ana)!;
        Assert.False(after.HasPendingEdit);
        Assert.Equal("Ana R.", after.Profile.DisplayName);
        Assert.Equal(0, _world.ProfileStore.PendingCount);
    }

    [Fact]
    public void When_the_server_has_a_newer_profile_the_local_edit_is_dropped()
    {
        _world.ProfileStore.Save(_ana, "Ana R.", null, null);
        AstronautProfile newer = TestWorld.ServerProfile("ana", TestWorld.Now.AddHours(1)) with { DisplayName = "Cambiada en tierra" };

        _world.ProfileStore.MarkSynced(_ana, newer);

        ProfileView after = _world.ProfileStore.Get(_ana)!;
        Assert.False(after.HasPendingEdit);
        Assert.Equal("Cambiada en tierra", after.Profile.DisplayName);
    }

    [Fact]
    public void Mark_all_synced_clears_every_pending_edit_and_keeps_the_values()
    {
        _world.ProfileStore.Save(_ana, "Ana R.", null, null);
        _world.ProfileStore.Save(TestWorld.IdOf("luis"), "Luis O.", null, null);

        int synced = _world.ProfileStore.MarkAllSynced();

        Assert.Equal(2, synced);
        Assert.Equal(0, _world.ProfileStore.PendingCount);
        Assert.Equal("Ana R.", _world.ProfileStore.Get(_ana)!.Profile.DisplayName);
        Assert.Equal(0, _world.ProfileStore.MarkAllSynced());
    }

    [Fact]
    public void Changed_is_delivered_on_the_ui_context_after_each_write()
    {
        QueueingSynchronizationContext ui = new();
        LocalProfileStore store = _world.NewProfileStore(ui);
        int raised = 0;
        store.Changed += (_, _) => raised++;

        store.Save(_ana, "Ana R.", null, null);
        Assert.Equal(0, raised);
        ui.RunPending();
        Assert.Equal(1, raised);

        store.MarkAllSynced();
        ui.RunPending();
        Assert.Equal(2, raised);
    }

    [Fact]
    public void The_edit_survives_restarting_the_app()
    {
        _world.ProfileStore.Save(_ana, "Ana R.", "Cometa", "nota");

        LocalProfileStore reopened = _world.NewProfileStore(null);

        Assert.Equal("Ana R.", reopened.Get(_ana)!.Profile.DisplayName);
        Assert.True(reopened.Get(_ana)!.HasPendingEdit);
    }
}

public sealed class ProfileViewModelTests
{
    private static ProfileViewModel Create(TestWorld world) => new(world.ProfileStore, world.Session, world.Clock);

    // ---- Ver ----

    [Fact]
    public void Shows_the_saved_profile_and_what_cannot_be_edited()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        Assert.True(profile.HasProfile);
        Assert.Equal("Ana Rivera", profile.DisplayName);
        Assert.Equal("Comet", profile.CallSign);
        Assert.Equal("Alergia a la penicilina", profile.Notes);
        Assert.Equal("ana", profile.UserName);
        Assert.Equal("Artemis Demo 1 · estación espacial", profile.MissionText);
        Assert.Equal("Día 101 de la misión", profile.MissionDayText);
        Assert.False(string.IsNullOrWhiteSpace(profile.DateOfBirthText));
        Assert.Contains("1988", profile.DateOfBirthText);
    }

    [Fact]
    public void Starts_clean_and_synced()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        Assert.False(profile.IsDirty);
        Assert.False(profile.CanSave);
        Assert.False(profile.HasPendingEdit);
        Assert.Equal("Perfil sincronizado.", profile.SyncText);
        Assert.StartsWith("Última edición: ", profile.UpdatedText);
        Assert.DoesNotContain("Hoy", profile.UpdatedText);
    }

    [Fact]
    public void Without_a_session_there_is_no_profile()
    {
        ProfileViewModel profile = Create(new TestWorld(signedInUser: null));

        Assert.False(profile.HasProfile);
        Assert.False(profile.CanSave);
    }

    [Fact]
    public void A_profile_without_birth_date_does_not_show_one()
    {
        TestWorld world = new("ana");
        world.ProfileStore.MarkSynced(TestWorld.IdOf("ana"), TestWorld.ServerProfile("ana") with { DateOfBirth = null });

        Assert.Null(Create(world).DateOfBirthText);
    }

    // ---- Editar ----

    [Fact]
    public void Editing_a_field_makes_it_dirty_and_saveable()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        profile.CallSign = "Cometa";

        Assert.True(profile.IsDirty);
        Assert.True(profile.CanSave);
        Assert.True(profile.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Typing_the_same_value_again_or_only_spaces_is_not_a_change()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        profile.DisplayName = "  Ana Rivera  ";

        Assert.False(profile.IsDirty);
        Assert.False(profile.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_an_error_and_cannot_be_saved(string name)
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        profile.DisplayName = name;

        Assert.Equal("El nombre es obligatorio.", profile.DisplayNameError);
        Assert.False(profile.CanSave);
        Assert.False(profile.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Fields_over_their_limit_show_an_error_and_block_saving()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        profile.DisplayName = new string('a', 101);
        Assert.Contains("100", profile.DisplayNameError);
        profile.DisplayName = "Ana";

        profile.CallSign = new string('a', 31);
        Assert.Contains("30", profile.CallSignError);
        Assert.False(profile.CanSave);
        profile.CallSign = "ok";

        profile.Notes = new string('a', 501);
        Assert.Contains("500", profile.NotesError);
        Assert.False(profile.CanSave);

        profile.Notes = "ok";
        Assert.True(profile.CanSave);
    }

    [Fact]
    public void The_notes_counter_follows_the_text()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        profile.Notes = "hola";

        Assert.EndsWith("4 / 500", profile.NotesCounterText.Replace(' ', ' '));
    }

    [Fact]
    public void The_fields_use_the_contract_limits()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));

        Assert.Equal((100, 30, 500), (profile.DisplayNameMaxLength, profile.CallSignMaxLength, profile.NotesMaxLength));
    }

    // ---- Guardar sin conexión y enviar en el siguiente sync ----

    [Fact]
    public void Saving_offline_keeps_the_edit_pending_and_says_it_goes_out_on_the_next_sync()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.DisplayName = "Ana R.";
        profile.CallSign = "Cometa";
        profile.Notes = "Sin alergias";

        profile.SaveCommand.Execute(null);

        Assert.Null(profile.Error);
        Assert.Equal("Cambios guardados en este equipo.", profile.SavedText);
        Assert.True(profile.HasPendingEdit);
        Assert.Contains("próxima sincronización", profile.SyncText);
        Assert.StartsWith("Última edición: Hoy", profile.UpdatedText);
        Assert.Equal(1, world.Sync.PendingCount);
        UpdateProfileRequest pending = world.ProfileStore.GetPendingEdit(TestWorld.IdOf("ana"))!;
        Assert.Equal(new UpdateProfileRequest("Ana R.", "Cometa", "Sin alergias", TestWorld.Now), pending);
    }

    [Fact]
    public void After_saving_there_is_nothing_left_to_save()
    {
        ProfileViewModel profile = Create(new TestWorld("ana", online: false));
        profile.CallSign = "Cometa";

        profile.SaveCommand.Execute(null);

        Assert.False(profile.IsDirty);
        Assert.False(profile.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void The_pending_edit_goes_out_when_the_link_returns_and_the_screen_follows_without_losing_what_is_typed()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.CallSign = "Cometa";
        profile.SaveCommand.Execute(null);
        profile.Notes = "algo que sigo escribiendo";

        world.Connectivity.Set(true);

        Assert.False(profile.HasPendingEdit);
        Assert.Equal("Perfil sincronizado.", profile.SyncText);
        Assert.Equal("algo que sigo escribiendo", profile.Notes);
        Assert.Equal(0, world.Sync.PendingCount);
    }

    [Fact]
    public void Saving_online_is_sent_right_away_by_the_sync()
    {
        TestWorld world = new("ana", online: true);
        ProfileViewModel profile = Create(world);
        profile.DisplayName = "Ana R.";

        profile.SaveCommand.Execute(null);

        Assert.Equal(0, world.ProfileStore.PendingCount);
        Assert.False(world.ProfileStore.Get(TestWorld.IdOf("ana"))!.HasPendingEdit);
    }

    [Fact]
    public void The_name_in_the_top_bar_follows_the_saved_name()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        int changes = 0;
        world.Session.Changed += (_, _) => changes++;
        profile.DisplayName = "Ana R.";

        profile.SaveCommand.Execute(null);

        Assert.Equal("Ana R.", world.Session.CurrentUser!.DisplayName);
        Assert.Equal(TestWorld.IdOf("ana"), world.Session.CurrentUser.Id);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Clearing_the_optional_fields_saves_them_as_empty()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.CallSign = "";
        profile.Notes = "";

        profile.SaveCommand.Execute(null);

        UpdateProfileRequest pending = world.ProfileStore.GetPendingEdit(TestWorld.IdOf("ana"))!;
        Assert.Null(pending.CallSign);
        Assert.Null(pending.Notes);
    }

    // ---- Solo el propio perfil ----

    [Fact]
    public void An_astronaut_edits_only_their_own_profile()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.DisplayName = "Ana R.";

        profile.SaveCommand.Execute(null);

        Assert.Equal("Luis Ortega", world.ProfileStore.Get(TestWorld.IdOf("luis"))!.Profile.DisplayName);
        Assert.Null(world.ProfileStore.GetPendingEdit(TestWorld.IdOf("luis")));
        Assert.Null(world.ProfileStore.GetPendingEdit(TestWorld.IdOf("mara")));
    }

    [Fact]
    public void Another_astronaut_on_the_same_computer_sees_their_own_profile()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel ana = Create(world);
        ana.DisplayName = "Ana R.";
        ana.SaveCommand.Execute(null);

        world.Session.SignOut();
        world.Session.SignIn(DemoAccounts.All.First(a => a.UserName == "luis"));
        ProfileViewModel luis = Create(world);

        Assert.Equal("Luis Ortega", luis.DisplayName);
        Assert.False(luis.HasPendingEdit);
    }

    [Fact]
    public void Saving_without_a_session_is_refused()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.DisplayName = "Otro";
        world.Session.SignOut();

        profile.SaveCommand.Execute(null);

        Assert.NotNull(profile.Error);
        Assert.Equal(0, world.ProfileStore.PendingCount);
    }

    [Fact]
    public void If_the_local_account_has_vanished_saving_explains_what_to_do()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.DisplayName = "Otro";
        world.Session.SignOut();
        world.Session.SignIn(new SessionUser(Guid.NewGuid(), "fantasma", "Fantasma"));

        profile.SaveCommand.Execute(null);

        Assert.Contains("Vuelve a iniciar sesión", profile.Error);
    }

    // ---- Descartar ----

    [Fact]
    public void Discard_goes_back_to_the_saved_values()
    {
        ProfileViewModel profile = Create(new TestWorld("ana"));
        profile.DisplayName = "Cambiado";
        profile.Notes = "otro";

        profile.DiscardCommand.Execute(null);

        Assert.Equal("Ana Rivera", profile.DisplayName);
        Assert.Equal("Alergia a la penicilina", profile.Notes);
        Assert.False(profile.IsDirty);
        Assert.False(profile.CanSave);
    }

    [Fact]
    public void The_confirmation_disappears_when_the_user_keeps_editing()
    {
        ProfileViewModel profile = Create(new TestWorld("ana", online: false));
        profile.CallSign = "Cometa";
        profile.SaveCommand.Execute(null);
        Assert.NotNull(profile.SavedText);

        profile.Notes = "más";

        Assert.Null(profile.SavedText);
    }

    [Fact]
    public void Disposing_stops_following_the_store()
    {
        TestWorld world = new("ana", online: false);
        ProfileViewModel profile = Create(world);
        profile.CallSign = "Cometa";
        profile.SaveCommand.Execute(null);
        profile.Dispose();

        world.Connectivity.Set(true);

        Assert.True(profile.HasPendingEdit);
    }
}

public sealed class SessionUpdateTests
{
    [Fact]
    public void Updating_changes_the_user_and_notifies_without_closing_the_session()
    {
        SessionContext session = new();
        session.SignIn(new SessionUser(TestWorld.IdOf("ana"), "ana", "Ana Rivera"), offline: true);
        int changes = 0;
        session.Changed += (_, _) => changes++;

        session.Update(new SessionUser(TestWorld.IdOf("ana"), "ana", "Ana R."));

        Assert.Equal("Ana R.", session.CurrentUser!.DisplayName);
        Assert.True(session.IsOfflineSession);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Updating_needs_an_open_session_of_the_same_astronaut()
    {
        SessionContext session = new();
        Assert.Throws<InvalidOperationException>(() => session.Update(new SessionUser(Guid.NewGuid(), "x", "X")));

        session.SignIn(new SessionUser(TestWorld.IdOf("ana"), "ana", "Ana Rivera"));
        Assert.Throws<InvalidOperationException>(() => session.Update(new SessionUser(TestWorld.IdOf("luis"), "luis", "Luis")));
    }
}

public sealed class SyncStatusProviderTests
{
    [Fact]
    public void The_demo_sync_counts_profile_edits_as_pending_and_sends_them_when_the_link_returns()
    {
        TestWorld world = new("ana", online: false);
        world.ProfileStore.Save(TestWorld.IdOf("ana"), "Ana R.", null, null);
        Assert.Equal(1, world.Sync.PendingCount);
        Assert.Equal(world.Store.LastSyncedAt, world.Sync.LastSyncAt);
        world.Clock.Now = TestWorld.Now.AddHours(2);

        world.Connectivity.Set(true);

        Assert.Equal(0, world.Sync.PendingCount);
        Assert.Equal(TestWorld.Now.AddHours(2), world.Sync.LastSyncAt);
    }

    [Fact]
    public void The_demo_sync_adds_check_ups_messages_and_profile_edits()
    {
        TestWorld world = new("ana", online: false);
        world.Store.QueueMessage(TestWorld.IdOf("ana"), "hola");
        world.ProfileStore.Save(TestWorld.IdOf("ana"), "Ana R.", null, null);

        Assert.Equal(2, world.Sync.PendingCount);
    }

    [Fact]
    public void The_real_server_provider_reports_pending_without_flushing_even_when_online()
    {
        // Sin la sincronización simulada: así se comporta la app con la Crew API real hasta que exista el motor de sync (#32).
        TestWorld world = new("ana", online: false, demoSync: false);
        PendingOnlySyncStatusProvider live = new(world.Store, world.ProfileStore);
        int changes = 0;
        live.Changed += (_, _) => changes++;

        world.ProfileStore.Save(TestWorld.IdOf("ana"), "Ana R.", null, null);
        world.Store.QueueMessage(TestWorld.IdOf("ana"), "hola");

        Assert.Equal(2, live.PendingCount);
        Assert.Equal(2, changes);
        world.Connectivity.Set(true);
        Assert.Equal(2, live.PendingCount);
        Assert.Equal(world.Store.LastSyncedAt, live.LastSyncAt);
    }
}
