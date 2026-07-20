namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>Immutable audit trail entry for administrative and launch actions.</summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Username { get; set; } = System.Environment.UserName;
    public string ComputerName { get; set; } = System.Environment.MachineName;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public bool Succeeded { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string ApplicationVersion { get; set; } = "1.0.0";
}
