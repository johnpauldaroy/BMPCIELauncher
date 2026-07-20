using Microsoft.EntityFrameworkCore;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Database;

/// <summary>
/// Ensures the database and its schema exist. Fresh installs intentionally start with no
/// applications — an administrator imports a configuration before use (Policies &amp; Setup →
/// Import config).
/// </summary>
public static class DataSeeder
{
    public static async Task EnsureCreatedAndSeededAsync(LauncherDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        await EnsureLaunchBrowserColumnAsync(db, ct);

        // EnsureCreated does not add columns to a pre-existing database, so backfill any
        // presentation columns added after the schema first shipped (dashboard icon/description).
        await EnsureColumnAsync(db, "IconKey", "TEXT NOT NULL DEFAULT 'App'", ct);
        await EnsureColumnAsync(db, "Description", "TEXT NULL", ct);

        // No default applications are seeded; the dashboard starts empty until a config is imported.
    }

    private static async Task EnsureLaunchBrowserColumnAsync(LauncherDbContext db, CancellationToken ct)
    {
        if (await ColumnExistsAsync(db, "LaunchBrowser", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Applications\" ADD COLUMN \"LaunchBrowser\" INTEGER NOT NULL DEFAULT 0;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Applications\" SET \"LaunchBrowser\" = 1 WHERE \"OpenIn\" = 1;", ct);
    }

    /// <summary>Adds a column to the Applications table if a pre-existing database is missing it.</summary>
    private static async Task EnsureColumnAsync(LauncherDbContext db, string column, string sqlType, CancellationToken ct)
    {
        if (await ColumnExistsAsync(db, column, ct))
            return;

        // SQLite ADD COLUMN cannot be parameterized. The column name and type are compile-time
        // constants supplied only by this class (never user input), so interpolation is safe.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE \"Applications\" ADD COLUMN \"{column}\" {sqlType};", ct);
#pragma warning restore EF1002
    }

    private static async Task<bool> ColumnExistsAsync(LauncherDbContext db, string column, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"Applications\");";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return false;
    }
}
