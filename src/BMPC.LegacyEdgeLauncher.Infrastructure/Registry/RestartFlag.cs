using BMPC.LegacyEdgeLauncher.Core.Constants;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Registry;

/// <summary>
/// File-based marker indicating that Microsoft Edge must be restarted before
/// policy or site-list changes take effect.
/// </summary>
public static class RestartFlag
{
    private static string FlagPath => Path.Combine(AppConstants.DataRoot, "edge-restart-pending.flag");

    public static void Set()
    {
        try
        {
            Directory.CreateDirectory(AppConstants.DataRoot);
            File.WriteAllText(FlagPath, DateTimeOffset.Now.ToString("O"));
        }
        catch
        {
            // Non-critical: the flag is advisory. Diagnostics will still detect stale policy state.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FlagPath)) File.Delete(FlagPath);
        }
        catch
        {
            // Non-critical.
        }
    }

    public static bool IsSet() => File.Exists(FlagPath);
}
