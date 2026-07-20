using BMPC.LegacyEdgeLauncher.Core.Constants;

namespace BMPC.LegacyEdgeLauncher.Core.Validation;

/// <summary>Normalizes and validates URLs before they are stored or launched.</summary>
public static class UrlNormalizer
{
    /// <summary>
    /// Normalizes user input into a canonical absolute URL.
    /// Adds https:// when no scheme is given, lower-cases scheme and host,
    /// strips fragments, and removes default ports.
    /// </summary>
    public static bool TryNormalize(string? input, out Uri? normalized, out string error)
    {
        normalized = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "URL is empty.";
            return false;
        }

        var candidate = input.Trim();
        if (!candidate.Contains("://"))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            error = $"'{input}' is not a valid URL.";
            return false;
        }

        if (!AppConstants.AllowedSchemes.Contains(uri.Scheme))
        {
            error = $"Scheme '{uri.Scheme}' is not allowed. Only http and https are permitted.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "URL has no host.";
            return false;
        }

        // Rebuild without fragment; Uri already lower-cases scheme/host and drops default ports.
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        normalized = builder.Uri;
        return true;
    }

    /// <summary>Host (lower case) plus explicit port when non-default — the site-list matching key.</summary>
    public static string NormalizedHost(Uri uri) =>
        uri.IsDefaultPort ? uri.Host.ToLowerInvariant() : $"{uri.Host.ToLowerInvariant()}:{uri.Port}";

    /// <summary>Normalizes a path rule: leading slash, no trailing slash, no query or fragment.</summary>
    public static string? NormalizePathRule(string? pathRule)
    {
        if (string.IsNullOrWhiteSpace(pathRule))
            return null;

        var p = pathRule.Trim();
        var cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) p = p[..cut];
        if (p.Length == 0 || p == "/")
            return null;
        if (!p.StartsWith('/')) p = "/" + p;
        return p.TrimEnd('/');
    }

    /// <summary>Builds a path-specific site-list rule from an application's absolute URL.</summary>
    public static string? InferPathRule(Uri applicationUri) =>
        NormalizePathRule(applicationUri.AbsolutePath);

    /// <summary>Final pre-launch safety check (section 14.1): scheme allowlist enforced on the exact URI.</summary>
    public static bool IsSafeToLaunch(Uri uri, out string reason)
    {
        if (!AppConstants.AllowedSchemes.Contains(uri.Scheme))
        {
            reason = $"Unsafe scheme '{uri.Scheme}' blocked.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            reason = "URL has no host.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates an Enterprise Mode Site List location before it becomes machine policy.
    /// Accepts HTTPS, loopback HTTP, and a local <c>file://</c> path (the standalone default,
    /// which lets Edge read the published sitelist.xml directly with no background service).
    /// </summary>
    public static bool TryValidateSiteListUrl(string? input, out Uri? siteListUri, out string error)
    {
        siteListUri = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 ||
            !Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            error = "The site-list URL must be a valid absolute URL.";
            return false;
        }

        if (uri.UserInfo.Length > 0 || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The site-list URL must not contain credentials or a fragment.";
            return false;
        }

        // A local file path: file:///C:/... — Host must be empty (no UNC to a remote host).
        var isLocalFile = uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) &&
                          string.IsNullOrEmpty(uri.Host);
        if (isLocalFile)
        {
            siteListUri = uri;
            return true;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "The site-list URL must be a valid absolute URL.";
            return false;
        }

        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                             uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            error = "The site-list URL must use HTTPS, HTTP for loopback, or a local file path.";
            return false;
        }

        siteListUri = uri;
        return true;
    }
}
