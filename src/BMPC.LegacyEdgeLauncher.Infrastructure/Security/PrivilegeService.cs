using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Results;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Security;

/// <summary>
/// Checks the current elevation state and relaunches the current executable elevated
/// via the standard UAC prompt (section 6.2). Never bypasses or suppresses UAC.
/// </summary>
public class PrivilegeService : IPrivilegeService
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt

    private readonly ILogger<PrivilegeService> _logger;

    public PrivilegeService(ILogger<PrivilegeService> logger)
    {
        _logger = logger;
    }

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task<ElevationResult> RelaunchElevatedAsync(
        ElevatedCommand command,
        IReadOnlyList<string>? arguments,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return Task.FromResult(new ElevationResult(false, "The current executable path could not be determined."));

        if (arguments is { Count: > 16 } ||
            arguments?.Any(a => a is null || a.Length > 2048 || a.Contains('\0')) == true)
        {
            return Task.FromResult(new ElevationResult(false, "The elevation request contains invalid arguments."));
        }

        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            // Security boundary: use an allowlisted command and tokenized arguments so
            // callers cannot supply an arbitrary elevated command line.
            psi.ArgumentList.Add(CommandToken(command));
            if (arguments is not null)
            {
                foreach (var argument in arguments)
                    psi.ArgumentList.Add(argument);
            }

            Process.Start(psi);
            _logger.LogInformation("Relaunched elevated command {Command} from {Exe}", command, exePath);
            return Task.FromResult(new ElevationResult(true, "A new elevated instance was started."));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return Task.FromResult(new ElevationResult(false,
                "Elevation was cancelled. Administrator changes cannot be made without approval."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relaunch elevated");
            return Task.FromResult(new ElevationResult(false, "Failed to start an elevated instance.", ex.Message));
        }
    }

    private static string CommandToken(ElevatedCommand command) => command switch
    {
        ElevatedCommand.ApplyPolicies => "apply-policies",
        ElevatedCommand.AllowPopups => "allow-popups",
        ElevatedCommand.PublishSiteList => "publish",
        ElevatedCommand.RollbackSiteList => "rollback-sitelist",
        ElevatedCommand.RestoreBackup => "restore-backup",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported elevated command.")
    };
}
