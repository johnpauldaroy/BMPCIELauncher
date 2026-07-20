using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.Services;

/// <summary>
/// Persists the dashboard tile order per Windows user. The order is a simple list of
/// application ids stored in the current user's roaming AppData, so each user keeps their
/// own arrangement and no elevation or shared-database write is needed.
/// </summary>
public class TileOrderStore
{
    private readonly ILogger<TileOrderStore> _logger;
    private readonly string _filePath;

    public TileOrderStore(ILogger<TileOrderStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "BMPC", "LegacyEdgeLauncher");
        _filePath = Path.Combine(dir, "tile-order.json");
    }

    /// <summary>Loads the saved id order. Returns an empty list if nothing has been saved yet.</summary>
    public IReadOnlyList<Guid> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<Guid>();

            var json = File.ReadAllText(_filePath);
            var ids = JsonSerializer.Deserialize<List<Guid>>(json);
            return ids ?? new List<Guid>();
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable order file must never block the dashboard; fall back to default order.
            _logger.LogWarning(ex, "Could not read tile order from {Path}; using default order.", _filePath);
            return Array.Empty<Guid>();
        }
    }

    /// <summary>Saves the given id order for the current user.</summary>
    public void Save(IEnumerable<Guid> orderedIds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(orderedIds, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save tile order to {Path}.", _filePath);
        }
    }
}
