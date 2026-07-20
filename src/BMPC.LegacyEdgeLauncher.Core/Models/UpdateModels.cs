namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>How the app applies a found update.</summary>
public enum UpdateMode
{
    /// <summary>Ask the user before downloading/installing (default).</summary>
    Prompt,
    /// <summary>Download and install without prompting.</summary>
    Silent,
    /// <summary>Only tell the user an update exists; do not download or install.</summary>
    NotifyOnly,
    /// <summary>Do not check for updates at all.</summary>
    Disabled
}

/// <summary>
/// Editable deployment configuration read at launch. IT can repoint the update source or
/// change behavior across units without rebuilding the application.
/// </summary>
public class UpdateConfig
{
    /// <summary>Base URL (folder) that hosts version.json and the installer, e.g. http://updates.bmpc.local/bmpc/.</summary>
    public string UpdateUrl { get; set; } = "http://updates.bmpc.local/bmpc/";

    /// <summary>How updates are applied when found.</summary>
    public UpdateMode Mode { get; set; } = UpdateMode.Prompt;

    /// <summary>Seconds to wait for the update server before giving up (must never block startup for long).</summary>
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// The remote update manifest (version.json) hosted at <see cref="UpdateConfig.UpdateUrl"/>.
/// </summary>
public class UpdateManifest
{
    /// <summary>Latest available version, e.g. "1.1.0".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Absolute or relative URL of the installer package for this version.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the installer, verified before it is run.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Optional human-readable release notes shown in the update prompt.</summary>
    public string? Notes { get; set; }

    /// <summary>Optional: if true, the app should refuse to keep running older versions.</summary>
    public bool Mandatory { get; set; }
}
