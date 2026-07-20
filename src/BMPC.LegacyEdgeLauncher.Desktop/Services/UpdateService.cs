using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Models;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.Services;

/// <summary>Outcome of an update check.</summary>
public record UpdateCheckResult(bool UpdateAvailable, UpdateManifest? Manifest, string? Message);

/// <summary>
/// Checks an internal HTTP location for a newer build and, when the user agrees, downloads,
/// verifies (SHA-256), and launches the per-user installer. Designed so a slow or unreachable
/// update server never blocks or crashes application startup.
/// </summary>
public class UpdateService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public UpdateService(IHttpClientFactory httpFactory, ILogger<UpdateService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Loads the editable deployment config, creating a default file if absent.</summary>
    public UpdateConfig LoadConfig()
    {
        try
        {
            if (File.Exists(AppConstants.UpdateConfigPath))
            {
                var json = File.ReadAllText(AppConstants.UpdateConfigPath);
                var cfg = JsonSerializer.Deserialize<UpdateConfig>(json, JsonOptions);
                if (cfg is not null) return cfg;
            }
            else
            {
                // Write a template so IT can discover and edit it.
                var template = new UpdateConfig();
                Directory.CreateDirectory(AppConstants.DataRoot);
                File.WriteAllText(AppConstants.UpdateConfigPath,
                    JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true }));
                return template;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read update-config.json; update checking uses defaults/off.");
        }
        return new UpdateConfig { Mode = UpdateMode.Disabled };
    }

    /// <summary>
    /// Best-effort: fetches version.json and reports whether a newer version exists.
    /// Never throws — any failure (server down, timeout, bad JSON) returns "no update".
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(UpdateConfig config, CancellationToken ct)
    {
        if (config.Mode == UpdateMode.Disabled || string.IsNullOrWhiteSpace(config.UpdateUrl))
            return new UpdateCheckResult(false, null, "Update checking is disabled.");

        try
        {
            var baseUri = new Uri(EnsureTrailingSlash(config.UpdateUrl));
            var manifestUri = new Uri(baseUri, "version.json");

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 30));

            var json = await http.GetStringAsync(manifestUri, ct);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                return new UpdateCheckResult(false, null, "The update manifest was empty or invalid.");

            var current = ParseVersion(AppConstants.ApplicationVersion);
            var remote = ParseVersion(manifest.Version);
            if (remote > current)
            {
                _logger.LogInformation("Update available: {Remote} (current {Current})", manifest.Version, AppConstants.ApplicationVersion);
                return new UpdateCheckResult(true, manifest, $"Version {manifest.Version} is available.");
            }

            return new UpdateCheckResult(false, manifest, "The application is up to date.");
        }
        catch (Exception ex)
        {
            // Unreachable server, DNS failure, timeout, malformed JSON — all non-fatal.
            _logger.LogInformation(ex, "Update check skipped (server unreachable or manifest unavailable).");
            return new UpdateCheckResult(false, null, "Could not reach the update server.");
        }
    }

    /// <summary>
    /// Downloads the installer named in the manifest, verifies its SHA-256, launches it, and
    /// returns true so the caller can shut the app down for the update to proceed.
    /// </summary>
    public async Task<(bool Started, string Message)> DownloadAndRunAsync(UpdateConfig config, UpdateManifest manifest, CancellationToken ct)
    {
        try
        {
            var baseUri = new Uri(EnsureTrailingSlash(config.UpdateUrl));
            var installerUri = new Uri(baseUri, manifest.Url);

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(10); // installers can be large

            var bytes = await http.GetByteArrayAsync(installerUri, ct);

            // Verify integrity before running anything.
            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var expected = manifest.Sha256.Trim().ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(actual),
                        System.Text.Encoding.ASCII.GetBytes(expected)))
                {
                    _logger.LogWarning("Update rejected: SHA-256 mismatch (expected {Exp}, got {Act}).", expected, actual);
                    return (false, "The update failed its integrity check and was not installed.");
                }
            }
            else
            {
                return (false, "The update manifest has no checksum; refusing to run an unverified installer.");
            }

            var fileName = Path.GetFileName(installerUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"bmpc-update-{manifest.Version}.exe";
            var target = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(target, bytes, ct);

            // Per-user installer: silent, no admin. /VERYSILENT is the Inno Setup convention.
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = "/VERYSILENT /NORESTART"
            };
            Process.Start(psi);

            return (true, $"Installing version {manifest.Version}. The application will restart.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or launch the update.");
            return (false, "The update could not be downloaded. Please try again later.");
        }
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>Lenient version parse: pads/truncates to a 4-part Version so "1.1" and "1.1.0.0" compare.</summary>
    private static Version ParseVersion(string raw)
    {
        var cleaned = new string(raw.Trim().Where(c => char.IsDigit(c) || c == '.').ToArray());
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var nums = parts.Take(4).Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
        while (nums.Count < 4) nums.Add(0);
        return new Version(nums[0], nums[1], nums[2], nums[3]);
    }
}
