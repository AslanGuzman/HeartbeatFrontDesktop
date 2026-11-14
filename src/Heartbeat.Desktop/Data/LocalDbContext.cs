using Microsoft.EntityFrameworkCore;

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
    }
}