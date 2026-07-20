using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Infrastructure.Backup;
using BMPC.LegacyEdgeLauncher.Infrastructure.Database;
using BMPC.LegacyEdgeLauncher.Infrastructure.Diagnostics;
using BMPC.LegacyEdgeLauncher.Infrastructure.Edge;
using BMPC.LegacyEdgeLauncher.Infrastructure.Registry;
using BMPC.LegacyEdgeLauncher.Infrastructure.Security;
using BMPC.LegacyEdgeLauncher.Infrastructure.Services;
using BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BMPC.LegacyEdgeLauncher.Infrastructure;

/// <summary>Single composition root shared by the Desktop app, the CLI, and the tests.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherInfrastructure(
        this IServiceCollection services, string? databasePath = null)
    {
        var dbPath = databasePath ?? AppConstants.DatabasePath;

        services.AddDbContextFactory<LauncherDbContext>(options =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            options.UseSqlite($"Data Source={dbPath}");
        });

        services.AddHttpClient();

        services.AddSingleton<IApplicationRepository, ApplicationRepository>();
        services.AddSingleton<IEdgePolicyService, EdgePolicyService>();
        services.AddSingleton<IInternetSecurityZoneService, TrustedSitesService>();
        services.AddSingleton<IEdgeContentPolicyService, EdgeContentPolicyService>();
        services.AddSingleton<ISiteListGenerator, SiteListGenerator>();
        services.AddSingleton<ISiteListValidator, SiteListValidator>();
        services.AddSingleton<ISiteListPublisher, SiteListPublisher>();
        services.AddSingleton<IEdgeLauncher, EdgeLauncher>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IConfigPortabilityService, ConfigPortabilityService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IPrivilegeService, PrivilegeService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();

        return services;
    }
}
