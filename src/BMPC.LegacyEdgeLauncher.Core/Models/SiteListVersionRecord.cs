namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>Audit record of a published Enterprise Mode Site List version.</summary>
public class SiteListVersionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long VersionNumber { get; set; }
    public string XmlHash { get; set; } = string.Empty;
    public string PublishedFilePath { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.Now;
    public string PublishedBy { get; set; } = string.Empty;
    public bool ValidationSucceeded { get; set; }
    public string? ValidationMessage { get; set; }
    public string? BackupFilePath { get; set; }
}
