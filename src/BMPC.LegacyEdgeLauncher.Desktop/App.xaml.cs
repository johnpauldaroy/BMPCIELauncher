using System.Windows;
using BMPC.LegacyEdgeLauncher.Desktop.ViewModels;
using BMPC.LegacyEdgeLauncher.Desktop.Views;
using BMPC.LegacyEdgeLauncher.Infrastructure;
using BMPC.LegacyEdgeLauncher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Information))
            .ConfigureServices(services =>
            {
                services.AddLauncherInfrastructure();
                services.AddSingleton<BMPC.LegacyEdgeLauncher.Desktop.Services.TileOrderStore>();
                services.AddSingleton<BMPC.LegacyEdgeLauncher.Desktop.Services.UpdateService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<LauncherViewModel>();
                services.AddSingleton<ApplicationsViewModel>();
                services.AddSingleton<SetupViewModel>();
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        try
        {
            var factory = _host.Services.GetRequiredService<IDbContextFactory<LauncherDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await DataSeeder.EnsureCreatedAndSeededAsync(db);
            }
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid();
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Desktop startup failed. Support reference: {CorrelationId}", correlationId);
            MessageBox.Show(
                $"The configuration database could not be opened.\n\nSupport reference: {correlationId}",
                "BMPC Legacy Edge Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();

        // Check for updates in the background so a slow/unreachable server never delays the UI.
        _ = CheckForUpdatesAsync();
    }

    /// <summary>Best-effort update check; prompts the user when a newer version is available.</summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var svc = _host!.Services.GetRequiredService<Services.UpdateService>();
            var config = svc.LoadConfig();
            if (config.Mode is Core.Models.UpdateMode.Disabled)
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 30) + 2));
            var result = await svc.CheckAsync(config, cts.Token);
            if (!result.UpdateAvailable || result.Manifest is null)
                return;

            var manifest = result.Manifest;

            if (config.Mode is Core.Models.UpdateMode.NotifyOnly)
            {
                MessageBox.Show(MainWindow!,
                    $"A new version ({manifest.Version}) is available.\n\n" +
                    (string.IsNullOrWhiteSpace(manifest.Notes) ? "" : manifest.Notes + "\n\n") +
                    "Please contact IT to update.",
                    "Update available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var proceed = config.Mode is Core.Models.UpdateMode.Silent;
            if (!proceed)
            {
                var choice = MessageBox.Show(MainWindow!,
                    $"Version {manifest.Version} is available (you have {Core.Constants.AppConstants.ApplicationVersion}).\n\n" +
                    (string.IsNullOrWhiteSpace(manifest.Notes) ? "" : manifest.Notes + "\n\n") +
                    "Install it now? The application will close briefly to update.",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                proceed = choice == MessageBoxResult.Yes;
            }

            if (!proceed) return;

            using var dlCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var (started, message) = await svc.DownloadAndRunAsync(config, manifest, dlCts.Token);
            if (started)
            {
                Shutdown(0); // let the installer replace files with the app closed
            }
            else
            {
                MessageBox.Show(MainWindow!, message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            // An update-check failure must never affect normal use.
            _host?.Services.GetRequiredService<ILogger<App>>()
                .LogInformation(ex, "Background update check failed silently.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
