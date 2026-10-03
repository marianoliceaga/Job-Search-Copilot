namespace JobPilot.Domain.Jobs;

/// <summary>Canonical form of posting URLs, so the same offer reached through different links is detected.</summary>
public static class UrlNormalizer
{
    private static readonly string[] TrackingPrefixes = ["utm_", "gh_", "lever-"];

    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "ref", "source", "src", "gh_src", "gh_jid", "lever-origin", "lever-source[]", "lever-source",
        "applicant_guid", "embed", "fbclid", "gclid", "_jtochash", "_jtocprof", "utm",
    };

    public static string Canonicalize(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return url.Trim().ToLowerInvariant();
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/application", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/apply", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..path.LastIndexOf('/')];
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (Raw: p, Name: Uri.UnescapeDataString(p.Split('=')[0])))
            .Where(p => !TrackingParameters.Contains(p.Name) && !TrackingPrefixes.Any(prefix => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Raw)
            .Order(StringComparer.Ordinal)
            .ToList();

        return "https://" + host + path.ToLowerInvariant() + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
    }
}
