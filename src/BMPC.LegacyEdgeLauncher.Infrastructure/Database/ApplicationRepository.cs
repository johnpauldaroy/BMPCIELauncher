using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Database;

public class ApplicationRepository : IApplicationRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;
    private readonly ILogger<ApplicationRepository> _logger;

    public ApplicationRepository(IDbContextFactory<LauncherDbContext> contextFactory, ILogger<ApplicationRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LegacyApplication>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Applications.Include(a => a.NeutralSites).AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
    }

    public async Task<LegacyApplication?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Applications.Include(a => a.NeutralSites).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<OperationResult> UpsertAsync(LegacyApplication application, CancellationToken ct)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);

            var duplicate = await db.Applications.AnyAsync(a =>
                a.Id != application.Id &&
                a.NormalizedHost == application.NormalizedHost &&
                (a.PathRule ?? string.Empty) == (application.PathRule ?? string.Empty), ct);
            if (duplicate)
                return OperationResult.Fail(
                    $"A rule for '{application.SiteListRuleUrl}' already exists. Duplicate site-list rules are not allowed.");

            var existing = await db.Applications.Include(a => a.NeutralSites)
                .FirstOrDefaultAsync(a => a.Id == application.Id, ct);
            if (existing is null)
            {
                db.Applications.Add(application);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(application);
                existing.UpdatedAt = DateTimeOffset.Now;
                db.NeutralSites.RemoveRange(existing.NeutralSites);
                existing.NeutralSites.Clear();
                foreach (var ns in application.NeutralSites)
                {
                    ns.LegacyApplicationId = existing.Id;
                    existing.NeutralSites.Add(ns);
                }
            }

            await db.SaveChangesAsync(ct);
            return OperationResult.Ok($"Application '{application.Name}' saved.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save application {Name}", application.Name);
            return OperationResult.Fail("Failed to save the application. The database may be locked or unavailable.", ex.Message);
        }
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            var app = await db.Applications.FindAsync(new object[] { id }, ct);
            if (app is null)
                return OperationResult.Fail("Application not found.");
            db.Applications.Remove(app);
            await db.SaveChangesAsync(ct);
            return OperationResult.Ok($"Application '{app.Name}' deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete application {Id}", id);
            return OperationResult.Fail("Failed to delete the application.", ex.Message);
        }
    }

    public async Task<long> GetNextSiteListVersionAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var max = await db.SiteListVersions.MaxAsync(v => (long?)v.VersionNumber, ct) ?? 0;
        return max + 1;
    }

    public async Task RecordSiteListVersionAsync(SiteListVersionRecord record, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.SiteListVersions.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SiteListVersionRecord>> GetSiteListHistoryAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.SiteListVersions.AsNoTracking().OrderByDescending(v => v.VersionNumber).ToListAsync(ct);
    }

    public async Task<Guid> SavePolicySnapshotAsync(PolicySnapshot snapshot, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.PolicySnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot.Id;
    }

    public async Task<PolicySnapshot?> GetPolicySnapshotAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.PolicySnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task MarkSnapshotRestoredAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.PolicySnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is not null)
        {
            snapshot.RestoredAt = DateTimeOffset.Now;
            await db.SaveChangesAsync(ct);
        }
    }
}
