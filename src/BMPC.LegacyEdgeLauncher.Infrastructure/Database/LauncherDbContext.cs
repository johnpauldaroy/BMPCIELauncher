using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Database;

public class LauncherDbContext : DbContext
{
    public LauncherDbContext(DbContextOptions<LauncherDbContext> options) : base(options) { }

    public DbSet<LegacyApplication> Applications => Set<LegacyApplication>();
    public DbSet<NeutralSite> NeutralSites => Set<NeutralSite>();
    public DbSet<SiteListVersionRecord> SiteListVersions => Set<SiteListVersionRecord>();
    public DbSet<PolicySnapshot> PolicySnapshots => Set<PolicySnapshot>();
    public DbSet<DiagnosticRun> DiagnosticRuns => Set<DiagnosticRun>();
    public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyApplication>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.NormalizedHost);
            e.HasMany(a => a.NeutralSites)
                .WithOne()
                .HasForeignKey(n => n.LegacyApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Ignore(a => a.SiteListRuleUrl);
            e.Ignore(a => a.LaunchBrowserDisplayName);
        });

        modelBuilder.Entity<SiteListVersionRecord>()
            .HasIndex(v => v.VersionNumber);

        modelBuilder.Entity<DiagnosticRun>()
            .HasMany(r => r.Results)
            .WithOne()
            .HasForeignKey(d => d.DiagnosticRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditEvent>()
            .HasIndex(a => a.Timestamp);
    }

    /// <summary>Creates a context pointing at the standard ProgramData database location.</summary>
    public static LauncherDbContext CreateDefault()
    {
        Directory.CreateDirectory(AppConstants.DatabaseDirectory);
        var options = new DbContextOptionsBuilder<LauncherDbContext>()
            .UseSqlite($"Data Source={AppConstants.DatabasePath}")
            .Options;
        return new LauncherDbContext(options);
    }
}
