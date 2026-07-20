using System.Xml.Linq;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Results;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;

/// <summary>
/// Validates a generated site-list document before it may be published (section 11.3):
/// well-formed XML, matching version, no duplicate rules, and only known
/// open-in / compat-mode values. A document that fails validation is never written to disk.
/// </summary>
public class SiteListValidator : ISiteListValidator
{
    private static readonly HashSet<string> ValidOpenIn =
        new(Enum.GetNames<OpenInMode>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidCompatModes =
        new(Enum.GetNames<CompatMode>(), StringComparer.Ordinal);

    public SiteListValidationResult Validate(SiteListDocument document)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        XDocument xml;
        try
        {
            xml = XDocument.Parse(document.Xml);
        }
        catch (Exception ex)
        {
            return new SiteListValidationResult(false,
                new[] { $"The site list is not well-formed XML: {ex.Message}" }, warnings);
        }

        var root = xml.Root;
        if (root is null || root.Name != "site-list")
        {
            errors.Add("The root element must be <site-list>.");
            return new SiteListValidationResult(false, errors, warnings);
        }

        var versionAttr = root.Attribute("version")?.Value;
        if (!long.TryParse(versionAttr, out var xmlVersion))
            errors.Add("The <site-list> version attribute is missing or not a number.");
        else if (xmlVersion != document.Version)
            errors.Add($"Version mismatch: document says {document.Version} but XML says {xmlVersion}.");

        var sites = root.Elements("site").ToList();
        if (sites.Count == 0)
            warnings.Add("The site list contains no rules. Publishing it will disable IE mode for all sites.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            var url = site.Attribute("url")?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                errors.Add("A <site> element has an empty url attribute.");
                continue;
            }

            if (!seen.Add(url))
                errors.Add($"Duplicate site rule: '{url}'. Each URL may appear only once.");

            if (url.Contains("://", StringComparison.Ordinal))
                warnings.Add($"Rule '{url}' includes a scheme. Host-based rules (no scheme) are recommended so both HTTP and HTTPS match.");

            if (url.Contains('*'))
                errors.Add($"Rule '{url}' contains a wildcard. Wildcards are not permitted (section 37.1 scoping rules).");

            var openIn = site.Element("open-in")?.Value;
            if (openIn is null || !ValidOpenIn.Contains(openIn))
                errors.Add($"Rule '{url}' has a missing or invalid <open-in> value ('{openIn}').");

            var compat = site.Element("compat-mode")?.Value;
            if (compat is not null && !ValidCompatModes.Contains(compat))
                errors.Add($"Rule '{url}' has an invalid <compat-mode> value ('{compat}').");
        }

        // Cross-check the entry collection so the in-memory list and XML never drift apart.
        if (sites.Count != document.Entries.Count)
            errors.Add($"Entry count mismatch: XML has {sites.Count} rules but the document lists {document.Entries.Count}.");

        return new SiteListValidationResult(errors.Count == 0, errors, warnings);
    }
}
