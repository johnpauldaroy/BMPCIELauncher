using System.Text.Json;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Services;

/// <summary>
/// Exports the application list to a portable JSON file and imports it back with merge
/// semantics (add new applications, update existing ones matched by their site-list rule).
/// Never deletes. Edge registry policies are intentionally out of scope.
/// </summary>
public class ConfigPortabilityService : IConfigPortabilityService
{
    private readonly IApplicationRepository _repository;
    private readonly ILogger<ConfigPortabilityService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigPortabilityService(IApplicationRepository repository, ILogger<ConfigPortabilityService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ConfigExportResult> ExportAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var apps = await _repository.GetAllAsync(cancellationToken);

            var export = new ConfigExport
            {
                ProductName = AppConstants.ApplicationName,
                ProductVersion = AppConstants.ApplicationVersion,
                ExportedAt = DateTimeOffset.Now,
                ExportedBy = System.Environment.UserName,
                Applications = apps.Select(ToExported).ToList()
            };

            var json = JsonSerializer.Serialize(export, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            return new ConfigExportResult(true,
                $"Exported {export.Applications.Count} application(s) to the config file.",
                export.Applications.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Config export failed for {Path}.", filePath);
            return new ConfigExportResult(false, "Failed to export the configuration.", TechnicalDetails: ex.Message);
        }
    }

    public async Task<ConfigImportResult> ImportAsync(string filePath, CancellationToken cancellationToken)
    {
        ConfigExport? export;
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            export = JsonSerializer.Deserialize<ConfigExport>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Config import could not read/parse {Path}.", filePath);
            return new ConfigImportResult(false, "The file is not a valid configuration export.", TechnicalDetails: ex.Message);
        }

        if (export is null)
            return new ConfigImportResult(false, "The configuration file was empty or unreadable.");

        if (export.SchemaVersion > ConfigExport.CurrentSchemaVersion)
            return new ConfigImportResult(false,
                $"This file was created by a newer version (schema v{export.SchemaVersion}). " +
                "Update the application before importing.");

        if (export.Applications.Count == 0)
            return new ConfigImportResult(true, "The configuration file contained no applications. Nothing to import.");

        // Map each existing application by its site-list rule so an imported item that targets
        // the same rule reuses that Id (an update) instead of colliding as a new insert.
        var existing = await _repository.GetAllAsync(cancellationToken);
        var existingByRule = existing
            .GroupBy(a => a.SiteListRuleUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var item in export.Applications)
        {
            if (!UrlNormalizer.TryNormalize(item.BaseUrl, out var uri, out var urlError) || uri is null)
            {
                skipped++;
                errors.Add($"'{item.Name}': {urlError}");
                continue;
            }

            var app = ToApplication(item, uri);

            // Reuse the existing Id when the rule already exists so UpsertAsync updates in place.
            var isUpdate = existingByRule.TryGetValue(app.SiteListRuleUrl, out var match);
            if (isUpdate && match is not null)
            {
                app.Id = match.Id;
                app.CreatedAt = match.CreatedAt;
                app.CreatedBy = match.CreatedBy;
            }

            var result = await _repository.UpsertAsync(app, cancellationToken);
            if (!result.Succeeded)
            {
                skipped++;
                errors.Add($"'{item.Name}': {result.Message}");
                continue;
            }

            if (isUpdate) updated++; else added++;
        }

        var message = $"Import complete: {added} added, {updated} updated" +
                      (skipped > 0 ? $", {skipped} skipped." : ".") +
                      " Publish the site list to apply changes to Edge.";
        return new ConfigImportResult(skipped == 0, message, added, updated, skipped,
            errors.Count > 0 ? string.Join("  ", errors) : null);
    }

    private static ExportedApplication ToExported(LegacyApplication a) => new()
    {
        Name = a.Name,
        Environment = a.Environment,
        BaseUrl = a.BaseUrl,
        PathRule = a.PathRule,
        OpenIn = a.OpenIn,
        LaunchBrowser = a.LaunchBrowser,
        CompatibilityMode = a.CompatibilityMode,
        IconKey = string.IsNullOrWhiteSpace(a.IconKey) ? AppIcons.DefaultKey : a.IconKey,
        Description = a.Description,
        Owner = a.Owner,
        Department = a.Department,
        SupportContact = a.SupportContact,
        Notes = a.Notes,
        IsEnabled = a.IsEnabled,
        NeutralSites = a.NeutralSites.Select(n => new ExportedNeutralSite
        {
            Url = n.Url,
            Notes = n.Notes,
            IsEnabled = n.IsEnabled
        }).ToList()
    };

    private static LegacyApplication ToApplication(ExportedApplication item, Uri normalized)
    {
        var host = UrlNormalizer.NormalizedHost(normalized);
        var user = System.Environment.UserName;
        return new LegacyApplication
        {
            Id = Guid.NewGuid(),
            Name = item.Name.Trim(),
            Environment = item.Environment,
            BaseUrl = normalized.AbsoluteUri,
            NormalizedHost = host,
            PathRule = UrlNormalizer.NormalizePathRule(item.PathRule),
            OpenIn = item.OpenIn,
            LaunchBrowser = item.LaunchBrowser,
            CompatibilityMode = item.CompatibilityMode,
            IconKey = string.IsNullOrWhiteSpace(item.IconKey) ? AppIcons.DefaultKey : item.IconKey,
            Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
            Owner = item.Owner,
            Department = item.Department,
            SupportContact = item.SupportContact,
            Notes = item.Notes,
            IsEnabled = item.IsEnabled,
            CreatedBy = user,
            UpdatedBy = user,
            NeutralSites = item.NeutralSites.Select(n => new NeutralSite
            {
                Url = n.Url,
                NormalizedHost = UrlNormalizer.TryNormalize(n.Url, out var nu, out _) && nu is not null
                    ? UrlNormalizer.NormalizedHost(nu)
                    : string.Empty,
                Notes = n.Notes,
                IsEnabled = n.IsEnabled,
                CreatedBy = user
            }).ToList()
        };
    }
}
