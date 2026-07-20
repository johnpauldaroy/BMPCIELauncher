namespace BMPC.LegacyEdgeLauncher.Core.Constants;

/// <summary>
/// The fixed catalog of dashboard icons an administrator can choose per application.
/// Glyphs are code points from the "Segoe MDL2 Assets" font shipped with Windows 10/11,
/// so no image assets need to be bundled. Keys are stored in the database; glyphs and
/// labels are presentation-only and can change without a data migration.
/// </summary>
public static class AppIcons
{
    public const string DefaultKey = "App";

    /// <summary>An icon choice: a stable <see cref="Key"/>, its display <see cref="Glyph"/>, and a friendly <see cref="Label"/>.</summary>
    public sealed record IconChoice(string Key, string Glyph, string Label);

    /// <summary>All selectable icons, in the order shown to the administrator.</summary>
    public static IReadOnlyList<IconChoice> All { get; } = new[]
    {
        new IconChoice("Bank",       "", "Bank / core banking"),
        new IconChoice("Loans",      "", "Loans / money"),
        new IconChoice("Accounting", "", "Accounting / calculator"),
        new IconChoice("Reports",    "",  "Reports / charts"),
        new IconChoice("Web",        "",  "Web portal"),
        new IconChoice("Utilities",  "", "Utilities / tools"),
        new IconChoice("Security",   "",  "Security / access"),
        new IconChoice("App",        "",  "Generic application"),
    };

    /// <summary>Resolves a stored key to its glyph, falling back to the generic app glyph.</summary>
    public static string GlyphFor(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            foreach (var choice in All)
                if (string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))
                    return choice.Glyph;
        }
        return GlyphFor(DefaultKey);
    }
}
