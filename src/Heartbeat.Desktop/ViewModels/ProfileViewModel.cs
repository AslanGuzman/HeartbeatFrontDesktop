using System.Globalization;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Perfil del astronauta con la sesión abierta (MR-12). Solo se edita el propio: la pantalla nunca recibe el Id de
/// otra persona. <c>displayName</c>, <c>callSign</c> y <c>notes</c> se pueden cambiar sin conexión; la edición queda
/// pendiente con su <c>updatedAt</c> y se envía en la siguiente sincronización.
/// </summary>
public sealed partial class ProfileViewModel : PageViewModel, IDisposable
{
    private readonly IProfileStore _store;
    private readonly ISessionContext _session;
    private readonly TimeProvider _clock;
    private readonly ICheckUpReminderSettingsStore _reminderSettings;
    private AstronautProfile? _loaded;

    public ProfileViewModel(
        IProfileStore store,
        ISessionContext session,
        TimeProvider clock,
        ICheckUpReminderSettingsStore reminderSettings)
    {
        _store = store;
        _session = session;
        _clock = clock;
        _reminderSettings = reminderSettings;

        DisplayName = string.Empty;
        CallSign = string.Empty;
        Notes = string.Empty;
        UserName = string.Empty;
        MissionText = string.Empty;
        MissionDayText = string.Empty;
        SyncText = string.Empty;
        UpdatedText = string.Empty;

        CheckUpReminderSettings reminder = _reminderSettings.Get();
        ReminderEnabled = reminder.Enabled;
        ReminderTimeText = reminder.Time.ToString("HH:mm", CultureInfo.InvariantCulture);

        Load();
        _store.Changed += OnStoreChanged;
    }

    public override string Title => "Perfil";

    // ---- Datos que no se editan ----

    public string UserName { get; private set; }

    public string MissionText { get; private set; }

    public string MissionDayText { get; private set; }

    /// <summary>Fecha de nacimiento en texto, o <c>null</c> si el servidor no la tiene.</summary>
    public string? DateOfBirthText { get; private set; }

    /// <summary>Hay un perfil que mostrar (la cuenta local lo tiene guardado).</summary>
    public bool HasProfile => _loaded is not null;

    // ---- Datos editables ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNameError), nameof(IsDirty), nameof(CanSave))]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CallSignError), nameof(IsDirty), nameof(CanSave))]
    public partial string CallSign { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotesError), nameof(NotesCounterText), nameof(IsDirty), nameof(CanSave))]
    public partial string Notes { get; set; }

    public int DisplayNameMaxLength => ProfileLimits.DisplayName;

    public int CallSignMaxLength => ProfileLimits.CallSign;

    public int NotesMaxLength => ProfileLimits.Notes;

    public string NotesCounterText => string.Create(CultureInfo.CurrentCulture, $"{Notes.Length} / {ProfileLimits.Notes}");

    public string? DisplayNameError =>
        string.IsNullOrWhiteSpace(DisplayName) ? "El nombre es obligatorio."
        : DisplayName.Trim().Length > ProfileLimits.DisplayName ? $"Máximo {ProfileLimits.DisplayName} caracteres."
        : null;

    public string? CallSignError => CallSign.Trim().Length > ProfileLimits.CallSign ? $"Máximo {ProfileLimits.CallSign} caracteres." : null;

    public string? NotesError => Notes.Trim().Length > ProfileLimits.Notes ? $"Máximo {ProfileLimits.Notes} caracteres." : null;

    /// <summary>Hay cambios sin guardar respecto al perfil mostrado.</summary>
    public bool IsDirty => _loaded is not null
        && (DisplayName.Trim() != _loaded.DisplayName
            || CallSign.Trim() != (_loaded.CallSign ?? string.Empty)
            || Notes.Trim() != (_loaded.Notes ?? string.Empty));

    public bool CanSave => IsDirty && DisplayNameError is null && CallSignError is null && NotesError is null;

    // ---- Recordatorio diario de check-up (P2-REMIND) ----

    [ObservableProperty]
    public partial bool ReminderEnabled { get; set; }

    [ObservableProperty]
    public partial string ReminderTimeText { get; set; }

    [ObservableProperty]
    public partial string? ReminderError { get; private set; }

    [ObservableProperty]
    public partial string? ReminderSavedText { get; private set; }

    // ---- Estado ----

    /// <summary>«Cambios guardados en este equipo: se enviarán en la próxima sincronización» o «Perfil sincronizado».</summary>
    [ObservableProperty]
    public partial string SyncText { get; private set; }

    [ObservableProperty]
    public partial bool HasPendingEdit { get; private set; }

    /// <summary>Cuándo se editó por última vez, por ejemplo «Hoy, 19:45».</summary>
    [ObservableProperty]
    public partial string UpdatedText { get; private set; }

    /// <summary>Confirmación tras guardar; <c>null</c> mientras no se haya guardado.</summary>
    [ObservableProperty]
    public partial string? SavedText { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    /// <summary>Guarda en el equipo (también sin conexión); la edición queda pendiente de sincronizar.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Error = null;
        SavedText = null;

        if (_session.CurrentUser is not { } user)
        {
            Error = "Inicia sesión para editar tu perfil.";
            return;
        }

        try
        {
            ProfileView saved = _store.Save(user.Id, DisplayName, CallSign, Notes);
            Apply(saved);

            // El nombre de la barra superior sigue al perfil.
            _session.Update(user with { DisplayName = saved.Profile.DisplayName });
            SavedText = "Cambios guardados en este equipo.";
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
        catch (InvalidOperationException)
        {
            Error = "No se encontró tu perfil en este equipo. Vuelve a iniciar sesión con conexión.";
        }
    }

    /// <summary>Vuelve a los valores guardados y descarta lo escrito.</summary>
    [RelayCommand]
    private void Discard()
    {
        Error = null;
        SavedText = null;
        if (_loaded is not null)
        {
            SetFields(_loaded);
        }
    }

    /// <summary>Guarda el horario del recordatorio en este equipo.</summary>
    [RelayCommand]
    private void SaveReminder()
    {
        ReminderError = null;
        ReminderSavedText = null;

        if (!TimeOnly.TryParseExact(
                ReminderTimeText.Trim(),
                ["H:mm", "HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly time))
        {
            ReminderError = "Escribe la hora en formato HH:mm, por ejemplo 09:00.";
            return;
        }

        try
        {
            _reminderSettings.Save(new CheckUpReminderSettings(ReminderEnabled, time));
            ReminderTimeText = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            ReminderSavedText = "Recordatorio guardado.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReminderError = "No se pudo guardar el recordatorio en este equipo.";
        }
    }

    partial void OnDisplayNameChanged(string value) => UpdateCommandState();

    partial void OnCallSignChanged(string value) => UpdateCommandState();

    partial void OnNotesChanged(string value) => UpdateCommandState();

    partial void OnReminderEnabledChanged(bool value) => ReminderSavedText = null;

    partial void OnReminderTimeTextChanged(string value)
    {
        ReminderError = null;
        ReminderSavedText = null;
    }

    private void UpdateCommandState()
    {
        SavedText = null;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void Load()
    {
        ProfileView? view = _session.CurrentUser is { } user ? _store.Get(user.Id) : null;
        if (view is null)
        {
            _loaded = null;
            OnPropertyChanged(nameof(HasProfile));
            return;
        }

        Apply(view);
    }

    private void Apply(ProfileView view)
    {
        AstronautProfile profile = view.Profile;
        _loaded = profile;

        UserName = profile.UserName;
        MissionText = $"{profile.Mission.Name} · {EnvironmentName(profile.Mission.Environment)}";
        MissionDayText = MissionDay(profile.Mission.StartsAt);
        DateOfBirthText = profile.DateOfBirth?.ToString("D", CultureInfo.CurrentCulture);
        HasPendingEdit = view.HasPendingEdit;
        SyncText = view.HasPendingEdit
            ? "Cambios guardados en este equipo. Se enviarán en la próxima sincronización."
            : "Perfil sincronizado.";
        UpdatedText = $"Última edición: {IndicatorPresentation.FormatWhen(profile.UpdatedAt, _clock)}";

        SetFields(profile);
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(MissionText));
        OnPropertyChanged(nameof(MissionDayText));
        OnPropertyChanged(nameof(DateOfBirthText));
        OnPropertyChanged(nameof(HasProfile));
    }

    private void SetFields(AstronautProfile profile)
    {
        DisplayName = profile.DisplayName;
        CallSign = profile.CallSign ?? string.Empty;
        Notes = profile.Notes ?? string.Empty;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    // El perfil cambió fuera de esta pantalla (la sincronización lo marcó como enviado): se actualiza el estado
    // sin pisar lo que el usuario esté escribiendo.
    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_session.CurrentUser is not { } user || _store.Get(user.Id) is not { } view || _loaded is null)
        {
            return;
        }

        HasPendingEdit = view.HasPendingEdit;
        SyncText = view.HasPendingEdit
            ? "Cambios guardados en este equipo. Se enviarán en la próxima sincronización."
            : "Perfil sincronizado.";
    }

    private static string EnvironmentName(string environment) => environment.ToLowerInvariant() switch
    {
        "station" => "estación espacial",
        "moon" => "Luna",
        "mars" => "Marte",
        _ => environment,
    };

    private string MissionDay(DateTimeOffset startsAt)
    {
        int day = (_clock.GetLocalNow().Date - TimeZoneInfo.ConvertTime(startsAt, _clock.LocalTimeZone).Date).Days + 1;
        return day >= 1
            ? $"Día {day} de la misión"
            : $"La misión comienza el {TimeZoneInfo.ConvertTime(startsAt, _clock.LocalTimeZone).ToString("d", CultureInfo.CurrentCulture)}";
    }
}
