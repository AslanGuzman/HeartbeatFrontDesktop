using System.IO;

using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.Tests;

public sealed class CheckUpReminderTests
{
    private static TestWorld WithSchedule(bool enabled = true, int hour = 9)
    {
        TestWorld world = new("ana");
        world.ReminderSettings.Settings = new CheckUpReminderSettings(enabled, new TimeOnly(hour, 0));
        return world;
    }

    [Fact]
    public void A_disabled_reminder_never_notifies()
    {
        TestWorld world = WithSchedule(enabled: false);
        world.Clock.Now = TestWorld.Now.AddHours(1);

        world.Reminder.Check();

        Assert.Equal(0, world.Notification.Shown);
    }

    [Fact]
    public void It_waits_until_the_configured_local_time()
    {
        TestWorld world = WithSchedule(hour: 13);

        world.Reminder.Check();
        Assert.Equal(0, world.Notification.Shown);

        world.Clock.Now = TestWorld.Now.AddHours(1);
        world.Reminder.Check();

        Assert.Equal(1, world.Notification.Shown);
    }

    [Fact]
    public void It_notifies_only_once_a_day()
    {
        TestWorld world = WithSchedule();
        world.Clock.Now = TestWorld.Now.AddHours(1);

        world.Reminder.Check();
        world.Reminder.Check();

        Assert.Equal(1, world.Notification.Shown);
    }

    [Fact]
    public void It_notifies_again_the_next_day()
    {
        TestWorld world = WithSchedule();
        world.Clock.Now = TestWorld.Now.AddHours(1);
        world.Reminder.Check();

        world.Clock.Now = TestWorld.Now.AddDays(1).AddHours(1);
        world.Reminder.Check();

        Assert.Equal(2, world.Notification.Shown);
    }

    [Fact]
    public void A_check_up_today_silences_the_reminder()
    {
        TestWorld world = WithSchedule();
        world.Clock.Now = TestWorld.Now.AddHours(1);
        ReadingInput input = new(IndicatorType.HeartRate, 70);
        world.Store.AddCheckUp(TestWorld.IdOf("ana"), [input], world.Evaluator.Evaluate([input]));

        world.Reminder.Check();

        Assert.Equal(0, world.Notification.Shown);
    }

    [Fact]
    public void Without_a_session_nothing_is_notified()
    {
        TestWorld world = new(signedInUser: null);
        world.ReminderSettings.Settings = new CheckUpReminderSettings(true, new TimeOnly(9, 0));
        world.Clock.Now = TestWorld.Now.AddHours(1);

        world.Reminder.Check();

        Assert.Equal(0, world.Notification.Shown);
        Assert.False(world.Reminder.IsPending);
    }
}

public sealed class FileCheckUpReminderSettingsStoreTests
{
    [Fact]
    public void The_schedule_round_trips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"heartbeat-reminder-{Guid.NewGuid():N}.json");
        try
        {
            FileCheckUpReminderSettingsStore store = new(path);
            Assert.Equal(CheckUpReminderSettings.Disabled, store.Get());

            store.Save(new CheckUpReminderSettings(true, new TimeOnly(21, 15)));

            Assert.Equal(new CheckUpReminderSettings(true, new TimeOnly(21, 15)), new FileCheckUpReminderSettingsStore(path).Get());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_damaged_file_reads_as_disabled()
    {
        string path = Path.Combine(Path.GetTempPath(), $"heartbeat-reminder-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "no es json");

            Assert.Equal(CheckUpReminderSettings.Disabled, new FileCheckUpReminderSettingsStore(path).Get());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
