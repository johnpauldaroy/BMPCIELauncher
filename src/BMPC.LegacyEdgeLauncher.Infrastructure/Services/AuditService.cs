using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Services;

/// <summary>
/// Append-only audit trail (section 20). Recording never throws — a failed audit write
/// is logged but must not break the operation being audited.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IDbContextFactory<LauncherDbContext> contextFactory, ILogger<AuditService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            db.AuditEvents.Add(auditEvent);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit event {Action}", auditEvent.Action);
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        DateTimeOffset? from, DateTimeOffset? to, string? action, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var query = db.AuditEvents.AsNoTracking();

        if (from is not null) query = query.Where(a => a.Timestamp >= from);
        if (to is not null) query = query.Where(a => a.Timestamp <= to);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);

        return await query.OrderByDescending(a => a.Timestamp).Take(1000).ToListAsync(ct);
    }
}
