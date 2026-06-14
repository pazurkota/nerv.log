using Microsoft.EntityFrameworkCore;
using nerv.log.Model;

namespace nerv.log.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LogEntry> Logs => Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEntry>()
            .HasIndex(x => new { x.TimeStamp, x.ServiceName });
    }
}