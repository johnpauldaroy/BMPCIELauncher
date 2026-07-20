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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
