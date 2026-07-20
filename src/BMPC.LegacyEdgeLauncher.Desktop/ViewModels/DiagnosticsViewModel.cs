using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>Diagnostics page (section 9.5/11). Available to standard users as read-only checks.</summary>
public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticService _diagnosticService;
    private readonly IApplicationRepository _repository;
    private readonly ILogger<DiagnosticsViewModel> _logger;

    public DiagnosticsViewModel(
        IDiagnosticService diagnosticService,
        IApplicationRepository repository,
        ILogger<DiagnosticsViewModel> logger)
    {
        _diagnosticService = diagnosticService;
        _repository = repository;
        _logger = logger;
    }

    public ObservableCollection<DiagnosticResult> Results { get; } = new();

    [ObservableProperty] private DiagnosticStatus? _overallStatus;
    [ObservableProperty] private string? _correlationId;

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Running diagnostics…";
            Results.Clear();

            // Prefer the seeded NATCCO app when present so the NATCCO-specific checks run.
            var apps = await _repository.GetAllAsync(CancellationToken.None);
            var target = apps.FirstOrDefault(a => a.IsEnabled)?.Id;

            DiagnosticReport report = await _diagnosticService.RunAsync(target, CancellationToken.None);
            foreach (var r in report.Results)
                Results.Add(r);

            OverallStatus = report.OverallStatus;
            CorrelationId = report.CorrelationId;
            StatusMessage = $"Diagnostics complete: {report.OverallStatus} (reference {report.CorrelationId}).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostics failed.");
            StatusMessage = "Diagnostics failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (Results.Count == 0)
        {
            StatusMessage = "Run diagnostics first.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("BMPC IE Launcher — Diagnostic report");
        sb.AppendLine($"Computer: {System.Environment.MachineName}   User: {System.Environment.UserName}");
        sb.AppendLine($"Overall: {OverallStatus}   Reference: {CorrelationId}");
        sb.AppendLine(new string('-', 60));
        foreach (var r in Results)
        {
            sb.AppendLine($"[{r.Status}] {r.CheckName}");
            sb.AppendLine($"    {r.FriendlyMessage}");
            if (!string.IsNullOrWhiteSpace(r.RecommendedAction))
                sb.AppendLine($"    Action: {r.RecommendedAction}");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            StatusMessage = "Diagnostic report copied to clipboard (no secrets included).";
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not copy report: " + ex.Message;
        }
    }
}
