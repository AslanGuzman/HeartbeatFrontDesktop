using System.IO;

using Heartbeat.Desktop.Data;
using Heartbeat.Shared;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

public sealed class LocalDatabaseTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<LocalDbContext> _options;

    public LocalDatabaseTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<LocalDbContext>().UseSqlite(_connection).Options;

        using LocalDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void All_the_local_tables_from_the_contract_exist()
    {
        using LocalDbContext db = new(_options);

        List<string> tables = db.Database
            .SqlQueryRaw<string>("select name as Value from sqlite_master where type = 'table'")
            .ToList();

        Assert.Contains("LocalAccounts", tables);
        Assert.Contains("CheckUps", tables);
        Assert.Contains("Readings", tables);
        Assert.Contains("Messages", tables);
        Assert.Contains("SyncCursors", tables);
        Assert.Contains("RangesCache", tables);
        Assert.Contains("TokenCache", tables);
    }

    [Fact]
    public void A_check_up_is_pending_by_default_and_stored_as_text()
    {
        Guid astronautId = Guid.NewGuid();
        Guid checkUpId = Guid.CreateVersion7();

        using (LocalDbContext db = new(_options))
        {
            db.CheckUps.Add(NewCheckUp(checkUpId, astronautId));
            db.SaveChanges();
        }

        using (LocalDbContext db = new(_options))
        {
            LocalCheckUp stored = db.CheckUps.Include(c => c.Readings).Single();

            Assert.Equal(SyncState.Pending, stored.SyncState);
            Assert.Null(stored.SyncError);
            LocalReading reading = Assert.Single(stored.Readings);
            Assert.Equal(185m, reading.Value);
            Assert.Equal(95m, reading.SecondaryValue);

            string raw = db.Database.SqlQueryRaw<string>("select SyncState as Value from CheckUps").Single();
            Assert.Equal("pending", raw);
        }
    }

    [Theory]
    [InlineData(SyncState.Synced, "synced")]
    [InlineData(SyncState.Rejected, "rejected")]
    public void Sync_state_round_trips_for_check_ups_and_messages(SyncState state, string expectedText)
    {
        Guid astronautId = Guid.NewGuid();

        using (LocalDbContext db = new(_options))
        {
            LocalCheckUp checkUp = NewCheckUp(Guid.CreateVersion7(), astronautId);
            checkUp.SyncState = state;
            checkUp.SyncError = state == SyncState.Rejected ? "Reading value outside valid range" : null;
            db.CheckUps.Add(checkUp);
            db.Messages.Add(new LocalMessage
            {
                Id = Guid.CreateVersion7(),
                AstronautId = astronautId,
                Direction = MessageDirection.ToMissionControl,
                Kind = MessageKind.Message,
                Subject = "Hola",
                Body = "Prueba",
                CreatedAt = DateTimeOffset.UtcNow,
                SyncState = state,
            });
            db.SaveChanges();
        }

        using (LocalDbContext db = new(_options))
        {
            Assert.Equal(state, db.CheckUps.Single().SyncState);
            Assert.Equal(state, db.Messages.Single().SyncState);
            Assert.Equal(expectedText, db.Database.SqlQueryRaw<string>("select SyncState as Value from Messages").Single());
        }
    }

    [Fact]
    public void A_message_is_pending_by_default()
    {
        Assert.Equal(SyncState.Pending, new LocalMessage().SyncState);
    }

    [Fact]
    public void Two_local_accounts_cannot_share_a_user_name()
    {
        using LocalDbContext db = new(_options);
        db.LocalAccounts.Add(new LocalAccount { AstronautId = Guid.NewGuid(), UserName = "a.rivera" });
        db.LocalAccounts.Add(new LocalAccount { AstronautId = Guid.NewGuid(), UserName = "a.rivera" });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void A_check_up_cannot_have_two_readings_of_the_same_indicator()
    {
        using LocalDbContext db = new(_options);
        LocalCheckUp checkUp = NewCheckUp(Guid.CreateVersion7(), Guid.NewGuid());
        checkUp.Readings.Add(new LocalReading
        {
            Id = Guid.CreateVersion7(),
            Indicator = IndicatorType.BloodPressure,
            Value = 120,
            SecondaryValue = 80,
        });
        db.CheckUps.Add(checkUp);

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Sync_cursor_ranges_cache_and_token_cache_round_trip()
    {
        Guid astronautId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using (LocalDbContext db = new(_options))
        {
            db.SyncCursors.Add(new SyncCursor { AstronautId = astronautId, LastPulledAt = now });
            db.RangesCache.Add(new RangesCache { Version = "2026.11.14-1", Json = "{}" });
            db.TokenCache.Add(new TokenCache
            {
                AstronautId = astronautId,
                AccessToken = "a",
                RefreshToken = "r",
                AccessTokenExpiresAt = now.AddMinutes(60),
                RefreshTokenExpiresAt = now.AddDays(14),
            });
            db.SaveChanges();
        }

        using (LocalDbContext db = new(_options))
        {
            Assert.Equal(now, db.SyncCursors.Single().LastPulledAt);
            Assert.Equal("2026.11.14-1", db.RangesCache.Single().Version);
            Assert.Equal(now.AddDays(14), db.TokenCache.Single().RefreshTokenExpiresAt);
        }
    }

    [Fact]
    public void Initialize_creates_the_folder_and_the_database_file()
    {
        string folder = Path.Combine(Path.GetTempPath(), "heartbeat-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "nested", "heartbeat-desktop.db");

        try
        {
            ServiceCollection services = new();
            services.AddDbContext<LocalDbContext>(o => o.UseSqlite(LocalDatabase.ConnectionString(path)));
            using ServiceProvider provider = services.BuildServiceProvider();

            LocalDatabase.Initialize(provider);

            Assert.True(File.Exists(path));

            // Una segunda llamada (siguiente arranque de la app) no debe fallar ni borrar nada.
            LocalDatabase.Initialize(provider);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_default_database_lives_under_the_user_profile_not_the_working_directory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.True(Path.IsPathRooted(LocalDatabase.DefaultPath));
        Assert.StartsWith(localAppData, LocalDatabase.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_reading_is_manual_without_a_device_by_default_and_stored_as_text()
    {
        using (LocalDbContext db = new(_options))
        {
            db.CheckUps.Add(NewCheckUp(Guid.CreateVersion7(), Guid.NewGuid()));
            db.SaveChanges();
        }

        using LocalDbContext read = new(_options);
        LocalReading reading = read.Readings.Single();

        Assert.Equal(ReadingSource.Manual, reading.Source);
        Assert.Null(reading.DeviceId);
        Assert.Equal("manual", read.Database.SqlQueryRaw<string>("select Source as Value from Readings").Single());
    }

    [Fact]
    public void A_wearable_reading_keeps_its_source_and_device()
    {
        using (LocalDbContext db = new(_options))
        {
            LocalCheckUp checkUp = NewCheckUp(Guid.CreateVersion7(), Guid.NewGuid());
            LocalReading reading = checkUp.Readings.Single();
            reading.Source = ReadingSource.Wearable;
            reading.DeviceId = "pixel-watch-3";
            db.CheckUps.Add(checkUp);
            db.SaveChanges();
        }

        using LocalDbContext read = new(_options);
        LocalReading stored = read.Readings.Single();

        Assert.Equal((ReadingSource.Wearable, "pixel-watch-3"), (stored.Source, stored.DeviceId));
        Assert.Equal("wearable", read.Database.SqlQueryRaw<string>("select Source as Value from Readings").Single());
    }

    [Fact]
    public void A_database_created_before_reading_source_is_upgraded_and_keeps_its_readings()
    {
        string folder = Path.Combine(Path.GetTempPath(), "heartbeat-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "heartbeat-desktop.db");

        try
        {
            ServiceCollection services = new();
            services.AddDbContext<LocalDbContext>(o => o.UseSqlite(LocalDatabase.ConnectionString(path)));
            using ServiceProvider provider = services.BuildServiceProvider();

            LocalDatabase.Initialize(provider);
            Guid checkUpId = Guid.CreateVersion7();
            using (IServiceScope scope = provider.CreateScope())
            {
                LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
                db.CheckUps.Add(NewCheckUp(checkUpId, Guid.NewGuid()));
                db.SaveChanges();

                // Deja la tabla como estaba antes de #9.
                db.Database.ExecuteSqlRaw("ALTER TABLE Readings DROP COLUMN Source");
                db.Database.ExecuteSqlRaw("ALTER TABLE Readings DROP COLUMN DeviceId");
            }

            LocalDatabase.Initialize(provider);
            LocalDatabase.Initialize(provider);

            using IServiceScope after = provider.CreateScope();
            LocalReading reading = after.ServiceProvider.GetRequiredService<LocalDbContext>().Readings.Single();
            Assert.Equal(checkUpId, reading.CheckUpId);
            Assert.Equal(185m, reading.Value);
            Assert.Equal((ReadingSource.Manual, (string?)null), (reading.Source, reading.DeviceId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static LocalCheckUp NewCheckUp(Guid id, Guid astronautId)
    {
        LocalCheckUp checkUp = new()
        {
            Id = id,
            AstronautId = astronautId,
            RecordedAt = DateTimeOffset.UtcNow,
            OverallStatus = HealthStatus.Critical,
            RangesVersion = "2026.11.14-1",
        };
        checkUp.Readings.Add(new LocalReading
        {
            Id = Guid.CreateVersion7(),
            Indicator = IndicatorType.BloodPressure,
            Value = 185,
            SecondaryValue = 95,
            Status = HealthStatus.Critical,
            BandIds = "bp-sys-critical-high,bp-dia-watch-high",
        });
        return checkUp;
    }
}
