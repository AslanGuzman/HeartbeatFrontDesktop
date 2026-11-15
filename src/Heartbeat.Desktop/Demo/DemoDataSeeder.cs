using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Siembra en la base local un historial de ejemplo para los tres astronautas de demostración, solo si aún no tienen
/// check-ups. Los check-ups sembrados ya figuran como sincronizados. Provisional, junto al acceso de demostración.
/// </summary>
public static class DemoDataSeeder
{
    public static void Seed(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        IHealthEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IHealthEvaluator>();
        DateTimeOffset now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        void Add(string user, TimeSpan ago, decimal sys, decimal dia, decimal hr, decimal sugar, decimal temp, decimal sleep, decimal mood)
        {
            Guid id = DemoAccounts.All.First(a => a.UserName == user).Id;
            List<ReadingInput> inputs =
            [
                new(IndicatorType.BloodPressure, sys, dia),
                new(IndicatorType.HeartRate, hr),
                new(IndicatorType.BloodSugar, sugar),
                new(IndicatorType.BodyTemperature, temp),
                new(IndicatorType.Sleep, sleep),
                new(IndicatorType.Mood, mood),
            ];

            LocalCheckUp checkUp = LocalHealthRecordStore.ToEntity(id, now - ago, inputs, evaluator.Evaluate(inputs), null);
            checkUp.SyncState = SyncState.Synced;
            checkUp.ReceivedAt = now - ago + TimeSpan.FromMinutes(2);
            db.CheckUps.Add(checkUp);
        }

        bool seeded = false;
        foreach (SessionUser account in DemoAccounts.All)
        {
            if (db.CheckUps.Any(c => c.AstronautId == account.Id))
            {
                continue;
            }

            seeded = true;
            switch (account.UserName)
            {
                case "ana":
                    Add("ana", TimeSpan.FromDays(3), 118, 76, 64, 92, 36.6m, 7.5m, 4);
                    Add("ana", TimeSpan.FromDays(2), 124, 82, 108, 96, 36.9m, 5.0m, 3);
                    Add("ana", TimeSpan.FromDays(1), 120, 78, 70, 90, 36.7m, 7.0m, 4);
                    break;
                case "luis":
                    Add("luis", TimeSpan.FromDays(2), 128, 84, 72, 105, 36.8m, 6.5m, 4);
                    Add("luis", TimeSpan.FromDays(1), 135, 88, 96, 52, 36.7m, 4.5m, 2);
                    break;
                case "mara":
                    Add("mara", TimeSpan.FromDays(1), 112, 72, 58, 88, 36.5m, 8.0m, 5);
                    break;
            }
        }

        if (seeded)
        {
            db.SaveChanges();
        }
    }
}
