using BMPC.LegacyEdgeLauncher.Cli.Commands;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Infrastructure;
using BMPC.LegacyEdgeLauncher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// bmpc-launcher — command-line front end for the BMPC Legacy Edge IE Mode Launcher.
// Shares the same Core/Infrastructure services (and audit trail) as the desktop app.

var services = new ServiceCollection();
services.AddLogging(logging =>
{
    logging.SetMinimumLevel(LogLevel.Warning);
    logging.AddSimpleConsole(o => o.SingleLine = true);
});
services.AddLauncherInfrastructure();
services.AddSingleton<CommandHandlers>();

await using var provider = services.BuildServiceProvider();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var factory = provider.GetRequiredService<IDbContextFactory<LauncherDbContext>>();
    await using (var db = await factory.CreateDbContextAsync(cts.Token))
    {
        await DataSeeder.EnsureCreatedAndSeededAsync(db, cts.Token);
    }

    var handlers = provider.GetRequiredService<CommandHandlers>();
    return await handlers.RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
catch (Exception ex)
{
    var correlationId = Guid.NewGuid();
    // Do not expose paths or registry/database details on the normal CLI surface.
    System.Diagnostics.Trace.TraceError("Unhandled CLI error {0}: {1}", correlationId, ex);
    Console.Error.WriteLine($"An unexpected error occurred. Support reference: {correlationId}");
    return 1;
}
