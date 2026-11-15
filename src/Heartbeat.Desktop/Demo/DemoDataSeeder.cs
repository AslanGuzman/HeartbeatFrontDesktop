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

        void AddEnvironment(string user, TimeSpan ago, params (string IndicatorId, decimal Value)[] values)
        {
            Guid astronautId = DemoAccounts.All.First(a => a.UserName == user).Id;
            DateTimeOffset recordedAt = now - ago;
            foreach ((string indicatorId, decimal value) in values)
            {
                db.EnvironmentReadings.Add(new LocalEnvironmentReading
                {
                    Id = Guid.CreateVersion7(),
                    AstronautId = astronautId,
                    RecordedAt = recordedAt,
                    ReceivedAt = recordedAt.AddMinutes(2),
                    IndicatorId = indicatorId,
                    Value = value,
                    Unit = IndicatorsCatalog.Find(indicatorId)?.Unit ?? string.Empty,
                    Simulated = true,
                });
            }
        }

        bool seeded = false;
        foreach (SessionUser account in DemoAccounts.All)
        {
            bool seededAccount = false;
            if (!db.CheckUps.Any(c => c.AstronautId == account.Id))
            {
                seededAccount = true;
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

            if (!db.EnvironmentReadings.Any(r => r.AstronautId == account.Id))
            {
                seededAccount = true;
                switch (account.UserName)
                {
                    case "ana":
                        AddEnvironment("ana", TimeSpan.FromDays(1),
                            ("co2", 2.2m), ("o2", 165m), ("pressure", 101.3m), ("temperature", 21.5m),
                            ("humidity", 45m), ("light", 320m), ("noise", 52m));
                        break;
                    case "luis":
                        AddEnvironment("luis", TimeSpan.FromDays(1),
                            ("co2", 5.4m), ("o2", 155m), ("pressure", 107m), ("temperature", 26.5m),
                            ("humidity", 65m), ("light", 150m), ("noise", 72m));
                        break;
                    case "mara":
                        AddEnvironment("mara", TimeSpan.FromDays(1),
                            ("co2", 3.4m), ("o2", 175m), ("pressure", 101.8m), ("temperature", 24.5m),
                            ("humidity", 62m), ("light", 220m), ("noise", 58m));
                        break;
                }
            }

            seeded |= seededAccount;
        }

        if (seeded)
        {
            db.SaveChanges();
        }
    }
}
