using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Registry;

/// <summary>
/// Manages the Edge PopupsAllowedForUrls allowlist (section 39). Entries are exact
/// scheme://host patterns — never wildcards — and every addition is snapshotted so it
/// can be removed again without touching entries this tool did not create.
/// </summary>
public class EdgeContentPolicyService : IEdgeContentPolicyService
{
    private readonly IApplicationRepository _repository;
    private readonly ILogger<EdgeContentPolicyService> _logger;

    public EdgeContentPolicyService(IApplicationRepository repository, ILogger<EdgeContentPolicyService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<ContentPolicyState> ReadAsync(Uri uri, CancellationToken ct)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AppConstants.PopupsAllowedKeyPath);
        var urls = key is null
            ? Array.Empty<string>()
            : key.GetValueNames()
                .Select(n => key.GetValue(n)?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray();

        var managed = IsLikelyDomainManaged();
        return Task.FromResult(new ContentPolicyState(urls, managed));
    }

    public async Task<ContentPolicyChangeResult> AllowPopupsForUrlAsync(Uri uri, CancellationToken ct)
    {
        var pattern = PatternFor(uri);
        var state = await ReadAsync(uri, ct);
        if (state.PopupsAllowedForUrls.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            return new ContentPolicyChangeResult(true, $"Pop-ups are already allowed for {pattern}.");

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(AppConstants.PopupsAllowedKeyPath)
                ?? throw new InvalidOperationException("Unable to open or create the PopupsAllowedForUrls key.");

            // Value names are a 1-based numeric list; take the first free slot.
            var used = key.GetValueNames()
                .Select(n => int.TryParse(n, out var i) ? i : 0)
                .Where(i => i > 0)
                .ToHashSet();
            var slot = 1;
            while (used.Contains(slot)) slot++;

            var snapshotId = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
            {
                Scope = "Machine",
                RegistryPath = AppConstants.PopupsAllowedKeyPath,
                ValueName = slot.ToString(),
                PreviousValue = null,
                PreviousValueType = null,
                NewValue = pattern,
                NewValueType = "String",
                ChangedBy = Environment.UserName
            }, ct);

            key.SetValue(slot.ToString(), pattern, RegistryValueKind.String);
            RestartFlag.Set();

            _logger.LogInformation("Allowed pop-ups for {Pattern} (slot {Slot})", pattern, slot);
            return new ContentPolicyChangeResult(true,
                $"Pop-ups are now allowed for {pattern}. Microsoft Edge must be restarted before this takes effect.",
                snapshotId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ContentPolicyChangeResult(false,
                "Administrator permission is required to change the pop-up allowlist. Restart the application as administrator.",
                null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to allow pop-ups for {Uri}", uri);
            return new ContentPolicyChangeResult(false, "Failed to update the pop-up allowlist.", null, ex.Message);
        }
    }

    public async Task<ContentPolicyChangeResult> RestoreAsync(Guid snapshotId, CancellationToken ct)
    {
        var snapshot = await _repository.GetPolicySnapshotAsync(snapshotId, ct);
        if (snapshot is null)
            return new ContentPolicyChangeResult(false, "Pop-up policy snapshot not found.");

        // Persisted rollback data is untrusted at this privilege boundary.
        if (!PolicySnapshotGuard.IsValidPopupSnapshot(snapshot, out var validationError))
        {
            _logger.LogWarning("Rejected unsafe pop-up policy snapshot {Id}: {Reason}", snapshotId, validationError);
            return new ContentPolicyChangeResult(false,
                "The pop-up policy snapshot is invalid or is not eligible for restore.");
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(snapshot.RegistryPath)
                ?? throw new InvalidOperationException("Unable to open the PopupsAllowedForUrls key.");

            if (snapshot.PreviousValue is null)
                key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            else
                key.SetValue(snapshot.ValueName, snapshot.PreviousValue, RegistryValueKind.String);

            await _repository.MarkSnapshotRestoredAsync(snapshotId, ct);
            RestartFlag.Set();
            return new ContentPolicyChangeResult(true, "Pop-up allowlist entry restored to its previous state.", snapshotId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ContentPolicyChangeResult(false,
                "Administrator permission is required to change the pop-up allowlist.", null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore pop-up snapshot {Id}", snapshotId);
            return new ContentPolicyChangeResult(false, "Failed to restore the pop-up allowlist entry.", null, ex.Message);
        }
    }

    public Task<bool> IsReportAutoOpenConfiguredAsync(CancellationToken ct)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AppConstants.AutoOpenFileTypesKeyPath);
        var present = key is null
            ? Array.Empty<string>()
            : key.GetValueNames()
                .Select(n => key.GetValue(n)?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.TrimStart('.'))
                .ToArray();

        var allPresent = AppConstants.ReportAutoOpenExtensions
            .All(ext => present.Contains(ext, StringComparer.OrdinalIgnoreCase));
        return Task.FromResult(allPresent);
    }

    public async Task<ContentPolicyChangeResult> EnableReportAutoOpenAsync(CancellationToken ct)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(AppConstants.AutoOpenFileTypesKeyPath)
                ?? throw new InvalidOperationException("Unable to open or create the AutoOpenFileTypes key.");

            var existing = key.GetValueNames()
                .Select(n => key.GetValue(n)?.ToString()?.TrimStart('.'))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toAdd = AppConstants.ReportAutoOpenExtensions
                .Where(ext => !existing.Contains(ext))
                .ToList();

            if (toAdd.Count == 0)
                return new ContentPolicyChangeResult(true,
                    "Reports are already set to open automatically in their viewer.");

            // Value names are a 1-based numeric list; fill the first free slots.
            var used = key.GetValueNames()
                .Select(n => int.TryParse(n, out var i) ? i : 0)
                .Where(i => i > 0)
                .ToHashSet();

            Guid? lastSnapshot = null;
            foreach (var ext in toAdd)
            {
                var slot = 1;
                while (used.Contains(slot)) slot++;
                used.Add(slot);

                lastSnapshot = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
                {
                    Scope = "Machine",
                    RegistryPath = AppConstants.AutoOpenFileTypesKeyPath,
                    ValueName = slot.ToString(),
                    PreviousValue = null,
                    PreviousValueType = null,
                    NewValue = ext,
                    NewValueType = "String",
                    ChangedBy = Environment.UserName
                }, ct);

                key.SetValue(slot.ToString(), ext, RegistryValueKind.String);
                _logger.LogInformation("Enabled auto-open for .{Ext} (slot {Slot})", ext, slot);
            }

            RestartFlag.Set();
            var list = string.Join(", ", toAdd.Select(e => "." + e));
            return new ContentPolicyChangeResult(true,
                $"Reports ({list}) will now open automatically in their viewer. " +
                "Microsoft Edge must be restarted before this takes effect.",
                lastSnapshot);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ContentPolicyChangeResult(false,
                "Administrator permission is required to change this policy. Restart the application as administrator.",
                null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable report auto-open.");
            return new ContentPolicyChangeResult(false, "Failed to enable automatic report opening.", null, ex.Message);
        }
    }

    /// <summary>Exact-origin pattern: scheme://host[:port]. Never a wildcard.</summary>
    private static string PatternFor(Uri uri) =>
        uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";

    private static bool IsLikelyDomainManaged()
    {
        try
        {
            return !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
