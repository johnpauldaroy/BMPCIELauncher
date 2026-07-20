namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>An authentication host emitted as &lt;open-in&gt;None&lt;/open-in&gt; so redirects keep the current engine.</summary>
public class NeutralSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LegacyApplicationId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string NormalizedHost { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string CreatedBy { get; set; } = string.Empty;
}
