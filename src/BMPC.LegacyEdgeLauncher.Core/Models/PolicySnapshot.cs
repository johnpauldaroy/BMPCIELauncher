namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>Backup of a single registry policy value taken before this tool modified it.</summary>
public class PolicySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Scope { get; set; } = "Machine";
    public string RegistryPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? PreviousValueType { get; set; }
    public string? NewValue { get; set; }
    public string? NewValueType { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset? RestoredAt { get; set; }
}
