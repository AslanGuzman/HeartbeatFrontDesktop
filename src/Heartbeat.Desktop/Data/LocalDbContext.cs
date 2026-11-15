using Heartbeat.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Heartbeat.Desktop.Data;

public class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options) { }

    public DbSet<LocalAccount> LocalAccounts => Set<LocalAccount>();
    public DbSet<LocalCheckUp> CheckUps => Set<LocalCheckUp>();
    public DbSet<LocalReading> Readings => Set<LocalReading>();
    public DbSet<LocalMessage> Messages => Set<LocalMessage>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<RangesCache> RangesCache => Set<RangesCache>();
    public DbSet<TokenCache> TokenCache => Set<TokenCache>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LocalAccount>().HasKey(x => x.AstronautId);
        b.Entity<SyncCursor>().HasKey(x => x.AstronautId);
        b.Entity<TokenCache>().HasKey(x => x.AstronautId);
        b.Entity<RangesCache>().HasKey(x => x.Id);
        b.Entity<LocalCheckUp>().HasMany(c => c.Readings).WithOne().HasForeignKey(r => r.CheckUpId);

        // Cada consulta del escritorio filtra por el astronauta con sesion (MR-12) y el motor de sync busca lo pendiente.
        b.Entity<LocalAccount>().HasIndex(x => x.UserName).IsUnique();
        b.Entity<LocalCheckUp>().HasIndex(x => new { x.AstronautId, x.RecordedAt });
        b.Entity<LocalCheckUp>().HasIndex(x => new { x.AstronautId, x.SyncState });
        b.Entity<LocalMessage>().HasIndex(x => new { x.AstronautId, x.CreatedAt });
        b.Entity<LocalMessage>().HasIndex(x => new { x.AstronautId, x.SyncState });

        // Un check-up tiene como maximo una lectura por indicador (CONTRACTS.md, seccion 3).
        b.Entity<LocalReading>().HasIndex(x => new { x.CheckUpId, x.Indicator }).IsUnique();

        // CONTRACTS.md guarda el estado de sync como pending / synced / rejected.
        b.Entity<LocalCheckUp>().Property(x => x.SyncState).HasConversion(SyncStateConverter).HasMaxLength(10);
        b.Entity<LocalMessage>().Property(x => x.SyncState).HasConversion(SyncStateConverter).HasMaxLength(10);

        // Origen de la lectura (manual / wearable), igual que en CONTRACTS.md.
        b.Entity<LocalReading>().Property(x => x.Source).HasConversion(ReadingSourceConverter).HasMaxLength(10);
        b.Entity<LocalReading>().Property(x => x.DeviceId).HasMaxLength(LocalReading.DeviceIdMaxLength);
    }

    private static readonly ValueConverter<ReadingSource, string> ReadingSourceConverter = new(
        source => source.ToString().ToLowerInvariant(),
        text => Enum.Parse<ReadingSource>(text, true));

    private static readonly ValueConverter<SyncState, string> SyncStateConverter = new(
        state => state.ToString().ToLowerInvariant(),
        text => Enum.Parse<SyncState>(text, true));
}