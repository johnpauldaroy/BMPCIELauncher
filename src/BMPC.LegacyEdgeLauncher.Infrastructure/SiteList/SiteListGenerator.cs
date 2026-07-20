using System.Xml.Linq;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;

/// <summary>
/// Generates an Enterprise Mode Site List (schema v2) document from the registered
/// applications and their neutral sites. Only enabled records are emitted, rules are
/// de-duplicated by URL, and neutral sites always open with &lt;open-in&gt;None&lt;/open-in&gt;
/// so authentication redirects keep the current engine.
/// </summary>
public class SiteListGenerator : ISiteListGenerator
{
    public SiteListDocument Generate(
        IReadOnlyCollection<LegacyApplication> applications,
        IReadOnlyCollection<NeutralSite> neutralSites,
        long version)
    {
        var entries = new List<SiteListEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in applications.Where(a => a.IsEnabled).OrderBy(a => a.SiteListRuleUrl, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(app.SiteListRuleUrl))
                entries.Add(new SiteListEntry(app.SiteListRuleUrl, app.OpenIn.ToString(), app.CompatibilityMode));
        }

        foreach (var neutral in neutralSites.Where(n => n.IsEnabled).OrderBy(n => n.NormalizedHost, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(neutral.NormalizedHost) && seen.Add(neutral.NormalizedHost))
                entries.Add(new SiteListEntry(neutral.NormalizedHost, nameof(OpenInMode.None), nameof(CompatMode.Default)));
        }

        var root = new XElement("site-list",
            new XAttribute("version", version),
            new XElement("created-by",
                new XElement("tool", AppConstants.ApplicationName),
                new XElement("version", AppConstants.ApplicationVersion),
                new XElement("date-created", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"))));

        foreach (var entry in entries)
        {
            var site = new XElement("site",
                new XAttribute("url", entry.Url),
                new XElement("open-in", entry.OpenIn));

            // <compat-mode> only matters for IE11 rules; Default is emitted explicitly for clarity.
            if (entry.OpenIn == nameof(OpenInMode.IE11))
                site.Add(new XElement("compat-mode", entry.CompatMode));

            root.Add(site);
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return new SiteListDocument(version, document.ToString(SaveOptions.None), entries);
    }
}
