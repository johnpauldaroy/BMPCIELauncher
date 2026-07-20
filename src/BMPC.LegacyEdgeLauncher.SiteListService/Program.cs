using System.Security.Cryptography;
using BMPC.LegacyEdgeLauncher.Core.Constants;

// BMPC Legacy Edge Site List Service (section 12): a tiny loopback-only HTTP host that
// serves the published Enterprise Mode Site List to Microsoft Edge on this machine.
// It binds 127.0.0.1 only — the site list is never exposed to the network.

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = AppConstants.ServiceName);

var port = builder.Configuration.GetValue("SiteList:Port", AppConstants.DefaultServicePort);
if (port is < 1024 or > 65535)
    throw new InvalidOperationException("SiteList:Port must be between 1024 and 65535.");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Logging.AddEventLog(settings => settings.SourceName = AppConstants.ServiceDisplayName);

var app = builder.Build();

app.Use(async (context, next) =>
{
    // Loopback binding alone does not prevent DNS rebinding through an attacker Host header.
    if (!string.Equals(context.Request.Host.Host, "127.0.0.1", StringComparison.Ordinal) ||
        context.Request.Host.Port != port)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Invalid Host header.", context.RequestAborted);
        return;
    }

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});

app.MapGet("/sitelist.xml", async (HttpContext context, ILogger<Program> logger) =>
{
    var path = AppConstants.SiteListFilePath;
    if (!File.Exists(path))
    {
        logger.LogWarning("Site list requested but no file exists at {Path}", path);
        return Results.NotFound("No site list has been published.");
    }

    // Edge polls this URL; make sure it never caches a stale version.
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    var xml = await File.ReadAllTextAsync(path, context.RequestAborted);
    return Results.Text(xml, "text/xml");
});

app.MapGet("/health", () =>
{
    var exists = File.Exists(AppConstants.SiteListFilePath);
    string? hash = null;
    DateTimeOffset? modified = null;
    if (exists)
    {
        using var stream = File.OpenRead(AppConstants.SiteListFilePath);
        hash = Convert.ToHexString(SHA256.HashData(stream));
        modified = File.GetLastWriteTime(AppConstants.SiteListFilePath);
    }

    return Results.Json(new
    {
        service = AppConstants.ServiceDisplayName,
        version = AppConstants.ApplicationVersion,
        siteListPublished = exists,
        siteListSha256 = hash,
        siteListModifiedAt = modified
    });
});

app.Run();
