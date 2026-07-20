using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Core.Models;

public class DiagnosticRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ApplicationId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public string OverallStatus { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public List<DiagnosticResult> Results { get; set; } = new();
}

public class DiagnosticResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiagnosticRunId { get; set; }
    public string CheckCode { get; set; } = string.Empty;
    public string CheckName { get; set; } = string.Empty;
    public DiagnosticStatus Status { get; set; }
    public string FriendlyMessage { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
    public string? RecommendedAction { get; set; }
}
