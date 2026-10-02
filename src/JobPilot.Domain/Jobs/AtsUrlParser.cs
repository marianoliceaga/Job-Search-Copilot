using System.Text.RegularExpressions;

namespace JobPilot.Domain.Jobs;

/// <summary>A job (or board) on a public ATS, recognised from its URL.</summary>
/// <param name="JobId">Null when the URL points to the whole board.</param>
public sealed record AtsReference(AtsKind Ats, string BoardToken, string? JobId);

public static partial class AtsUrlParser
{
    public static AtsReference? Parse(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host is "boards.greenhouse.io" or "job-boards.greenhouse.io" or "job-boards.eu.greenhouse.io" or "boards.eu.greenhouse.io")
        {
            // /{board} or /{board}/jobs/{id}
            if (segments.Length >= 1 && segments[0] != "embed")
            {
                return new AtsReference(AtsKind.Greenhouse, segments[0], segments.Length >= 3 && segments[1] == "jobs" ? segments[2] : null);
            }

            // /embed/job_app?for={board}&token={id}
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["for"] is { Length: > 0 } board ? new AtsReference(AtsKind.Greenhouse, board, query["token"]) : null;
        }

        if (host is "jobs.lever.co" or "jobs.eu.lever.co" && segments.Length >= 1)
        {
            return new AtsReference(AtsKind.Lever, segments[0], segments.Length >= 2 && GuidRegex().IsMatch(segments[1]) ? segments[1] : null);
        }

        if (host == "jobs.ashbyhq.com" && segments.Length >= 1)
        {
            return new AtsReference(AtsKind.Ashby, Uri.UnescapeDataString(segments[0]), segments.Length >= 2 && GuidRegex().IsMatch(segments[1]) ? segments[1] : null);
        }

        return null;
    }

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidRegex();
}
