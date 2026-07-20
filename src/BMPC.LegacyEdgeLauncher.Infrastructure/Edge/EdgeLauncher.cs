using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using BMPC.LegacyEdgeLauncher.Infrastructure.Registry;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Edge;

/// <summary>
/// Locates and launches Microsoft Edge. Every launch passes the exact URI through the
/// scheme allowlist (section 14.1) and hands it to msedge.exe as a single argument —
/// never through the shell — so no argument injection is possible.
/// </summary>
public class EdgeLauncher : IEdgeLauncher
{
    private const string EdgeAppPathsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe";

    private readonly ILogger<EdgeLauncher> _logger;

    public EdgeLauncher(ILogger<EdgeLauncher> logger)
    {
        _logger = logger;
    }

    public string? FindEdgeExecutable()
    {
        // Security boundary: HKCU is user-controlled and must never select an executable
        // that may later be launched by an elevated instance of this application.
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(EdgeAppPathsKey))
        {
            if (key?.GetValue(null) is string path && File.Exists(path))
                return path;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft\Edge\Application\msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public string? GetEdgeVersion()
    {
        var exe = FindEdgeExecutable();
        if (exe is null) return null;
        try
        {
            return FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    public bool IsEdgeRunning() => Process.GetProcessesByName("msedge").Length > 0;

    public Task<LaunchResult> OpenAsync(Uri url, BrowserLaunchMode browser, CancellationToken ct, string? compatibilityMode = null)
    {
        if (!UrlNormalizer.IsSafeToLaunch(url, out var reason))
        {
            _logger.LogWarning("Blocked launch of {Url}: {Reason}", url, reason);
            return Task.FromResult(new LaunchResult(false,
                "This URL was blocked by the launch safety check.", reason));
        }

        ct.ThrowIfCancellationRequested();

        return browser == BrowserLaunchMode.InternetExplorerLegacy
            ? Task.FromResult(OpenLegacyInternetExplorer(url, compatibilityMode))
            : Task.FromResult(OpenEdge(url, browser));
    }

    private LaunchResult OpenEdge(Uri url, BrowserLaunchMode browser)
    {
        var exe = FindEdgeExecutable();
        if (exe is null)
        {
            return new LaunchResult(false,
                "Microsoft Edge could not be found on this computer. Install Microsoft Edge and try again.",
                "msedge.exe not found via App Paths or default install locations.");
        }

        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(url.AbsoluteUri);
            Process.Start(psi);

            _logger.LogInformation("Launched Edge for {Url} using {BrowserMode}", url, browser);
            var mode = browser == BrowserLaunchMode.EdgeIeMode ? "Microsoft Edge (IE mode)" : "Microsoft Edge";
            return new LaunchResult(true, $"Opened {url.Host} in {mode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Edge for {Url}", url);
            return new LaunchResult(false, "Failed to start Microsoft Edge.", null, ex.Message);
        }
    }

    private LaunchResult OpenLegacyInternetExplorer(Uri url, string? compatibilityMode)
    {
        // Real Internet Explorer ignores the Enterprise Mode site list, so the document mode
        // must be set via FEATURE_BROWSER_EMULATION for iexplore.exe before navigating.
        // This is a per-executable setting: the last app launched wins while its window is open.
        TrySetBrowserEmulation(compatibilityMode);

        object? internetExplorer = null;
        try
        {
            var type = Type.GetTypeFromProgID("InternetExplorer.Application", throwOnError: false);
            if (type is null)
            {
                return new LaunchResult(false,
                    "Internet Explorer is not available on this computer. Select Edge (IE mode) instead.",
                    "Windows did not expose the InternetExplorer.Application COM server.");
            }

            internetExplorer = Activator.CreateInstance(type);
            if (internetExplorer is null)
                throw new COMException("Internet Explorer could not be created.");

            type.InvokeMember("Visible", BindingFlags.SetProperty, null, internetExplorer, new object[] { true });
            type.InvokeMember("Navigate", BindingFlags.InvokeMethod, null, internetExplorer,
                new object[] { url.AbsoluteUri });

            _logger.LogWarning("Launched unsupported legacy Internet Explorer COM automation for {Url}", url);
            return new LaunchResult(true, $"Opened {url.Host} in legacy Internet Explorer.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Legacy Internet Explorer is unavailable for {Url}", url);
            return new LaunchResult(false,
                "Internet Explorer could not be started. Select Edge (IE mode), which is supported on current Windows versions.",
                "The legacy Internet Explorer COM server is disabled or unavailable.", ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (internetExplorer is not null && Marshal.IsComObject(internetExplorer))
            {
                try { Marshal.FinalReleaseComObject(internetExplorer); }
                catch (InvalidComObjectException) { /* already released by the COM runtime */ }
            }
        }
    }

    /// <summary>
    /// Writes FEATURE_BROWSER_EMULATION for iexplore.exe so real IE renders the page in the
    /// application's configured document mode instead of defaulting to edge/IE11 mode.
    /// Written under HKCU (no elevation needed) for both 32- and 64-bit IE hosts.
    /// A null/Default/unmappable mode clears any value this tool set, restoring IE's default.
    /// </summary>
    private void TrySetBrowserEmulation(string? compatibilityMode)
    {
        const string emulationKey =
            @"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";
        const string wow6432Key =
            @"SOFTWARE\WOW6432Node\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";

        var value = EmulationValueFor(compatibilityMode);

        foreach (var path in new[] { emulationKey, wow6432Key })
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
                if (key is null) continue;

                if (value is null)
                    key.DeleteValue("iexplore.exe", throwOnMissingValue: false);
                else
                    key.SetValue("iexplore.exe", value.Value, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                // Never let an emulation-registry failure block the actual launch.
                _logger.LogWarning(ex, "Could not set IE browser emulation ({Mode}) at {Path}", compatibilityMode, path);
            }
        }

        if (value is not null)
            _logger.LogInformation("Set IE FEATURE_BROWSER_EMULATION to {Value} for mode {Mode}", value, compatibilityMode);
    }

    /// <summary>Maps an application's compatibility mode to its FEATURE_BROWSER_EMULATION DWORD.
    /// Returns null for Default/unknown so IE uses its normal document-mode selection.</summary>
    private static int? EmulationValueFor(string? compatibilityMode) => compatibilityMode switch
    {
        "IE11" => 11001,
        "IE10" => 10001,
        "IE9"  => 9999,
        "IE8"  => 8888,
        "IE7"  => 7000,
        // IE7Enterprise/IE8Enterprise are Edge-IE-mode concepts, not IE document modes.
        _ => null // Default, IE5, IE7Enterprise, IE8Enterprise, null, or anything unrecognized.
    };

    public async Task<OperationResult> RestartEdgeAsync(bool force, CancellationToken ct)
    {
        var processes = Process.GetProcessesByName("msedge");
        if (processes.Length == 0)
        {
            RestartFlag.Clear();
            return OperationResult.Ok("Microsoft Edge is not running. New policy settings will apply on next start.");
        }

        if (!force)
        {
            return OperationResult.Fail(
                $"Microsoft Edge is running ({processes.Length} processes). " +
                "Close Edge manually, or confirm a forced restart — unsaved work in open tabs may be lost.");
        }

        try
        {
            foreach (var p in processes)
            {
                try { p.CloseMainWindow(); } catch { /* window may already be gone */ }
            }

            // Give Edge a moment to shut down cleanly before killing stragglers.
            var deadline = DateTimeOffset.Now.AddSeconds(10);
            while (DateTimeOffset.Now < deadline && Process.GetProcessesByName("msedge").Length > 0)
                await Task.Delay(500, ct);

            foreach (var p in Process.GetProcessesByName("msedge"))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
            }

            RestartFlag.Clear();
            _logger.LogInformation("Microsoft Edge was closed so policy changes take effect");
            return OperationResult.Ok("Microsoft Edge was closed. Policy and site-list changes will apply when it starts again.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart Edge");
            return OperationResult.Fail("Failed to close Microsoft Edge.", ex.Message);
        }
    }
}
