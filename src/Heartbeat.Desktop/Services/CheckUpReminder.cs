namespace Heartbeat.Desktop.Services;

/// <summary>Decide si el check-up de rutina de hoy está pendiente y lanza el aviso una vez al día.</summary>
public sealed class CheckUpReminder(
    ICheckUpReminderSettingsStore settings,
    IHealthRecordStore records,
    ISessionContext session,
    ICheckUpNotification notification,
    TimeProvider clock)
{
    // shortcut: el "ya avisé hoy" vive en memoria; si la app se reinicia después de la hora, puede avisar otra vez.
    private (DateOnly Day, Guid AstronautId)? _notified;

    public CheckUpReminderSettings Settings => settings.Get();

    /// <summary>Hay una sesión abierta y aún no se registró ningún check-up hoy (hora local).</summary>
    public bool IsPending => session.CurrentUser is { } user && !HasCheckUpToday(user.Id);

    /// <summary>Muestra la notificación de Windows si ya pasó la hora y el check-up de hoy sigue pendiente.</summary>
    public void Check()
    {
        CheckUpReminderSettings current = settings.Get();
        if (!current.Enabled || session.CurrentUser is not { } user)
        {
            return;
        }

        DateTimeOffset now = clock.GetLocalNow();
        if (now.TimeOfDay < current.Time.ToTimeSpan() || HasCheckUpToday(user.Id))
        {
            return;
        }

        (DateOnly Day, Guid AstronautId) today = (DateOnly.FromDateTime(now.DateTime), user.Id);
        if (_notified == today)
        {
            return;
        }

        _notified = today;
        notification.Show();
    }

    private bool HasCheckUpToday(Guid astronautId)
    {
        DateOnly today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return records.GetCheckUps(astronautId)
            .Any(checkUp => LocalDay(checkUp.RecordedAt) == today);
    }

    private DateOnly LocalDay(DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone).DateTime);
}
