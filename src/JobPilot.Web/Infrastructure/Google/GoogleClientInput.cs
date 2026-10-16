using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobPilot.Web.Infrastructure.Google;

/// <summary>
/// What the wizard could read from the owner's input: the client JSON that Google Cloud offers to download when the client is
/// created, or a paste that holds both values. <see cref="Warning"/> is set when the JSON lacks the app's redirect URI.
/// </summary>
public sealed record GoogleClientInputResult(string? ClientId, string? ClientSecret, string? Error, string? Warning)
{
    public bool HasBoth => ClientId is not null && ClientSecret is not null;
}

/// <summary>Reads the OAuth client from Google Cloud's downloaded JSON or from pasted text, so the owner does not type it.</summary>
public static partial class GoogleClientInput
{
    /// <summary>The downloaded file is about 400 bytes; anything much larger is not a client file.</summary>
    public const long MaxFileBytes = 64 * 1024;

    /// <summary>Reads Google Cloud's client file (<c>client_secret_….json</c>).</summary>
    public static GoogleClientInputResult FromJson(string? json, string redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return Failure("El archivo no es el JSON del cliente. En Google Cloud, en el cliente creado, usá Download JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("El archivo no es el JSON del cliente. En Google Cloud, en el cliente creado, usá Download JSON.");
            }

            if (root.TryGetProperty("installed", out _))
            {
                return Failure("Ese cliente es de tipo Desktop (escritorio). JobSearch Copilot necesita uno de tipo Web application: crealo como dice el paso 4.");
            }

            if (!root.TryGetProperty("web", out var web) || web.ValueKind != JsonValueKind.Object)
            {
                return Failure("El archivo no tiene un cliente de tipo Web application. En Google Cloud, en el cliente creado, usá Download JSON.");
            }

            var id = Text(web, "client_id");
            var secret = Text(web, "client_secret");
            if (id is null || secret is null)
            {
                return Failure("El archivo no trae el Client ID y el Client secret. Descargalo de nuevo desde el cliente en Google Cloud.");
            }

            var redirects = web.TryGetProperty("redirect_uris", out var uris) && uris.ValueKind == JsonValueKind.Array
                ? uris.EnumerateArray().Where(u => u.ValueKind == JsonValueKind.String).Select(u => u.GetString()!).ToList()
                : [];
            var warning = redirects.Contains(redirectUri, StringComparer.Ordinal)
                ? null
                : $"El cliente no tiene la dirección {redirectUri} en Authorized redirect URIs: agregala en Google Cloud (paso 4) o Google va a responder \"redirect_uri_mismatch\".";
            return new GoogleClientInputResult(id, secret, null, warning);
        }
    }

    /// <summary>
    /// Reads pasted text: the client JSON, or text with both the Client ID and a <c>GOCSPX-</c> secret (as copied from the
    /// dialog Google shows after creating the client). Returns no values when the text is a single plain value.
    /// </summary>
    public static GoogleClientInputResult FromText(string? text, string redirectUri)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith('{'))
        {
            return FromJson(value, redirectUri);
        }

        var id = ClientIdPattern().Match(value);
        var secret = SecretPattern().Match(value);
        return new GoogleClientInputResult(id.Success ? id.Value : null, secret.Success ? secret.Value : null, null, null);
    }

    private static GoogleClientInputResult Failure(string error) => new(null, null, error, null);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    [GeneratedRegex(@"[0-9A-Za-z-]+\.apps\.googleusercontent\.com")]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex(@"GOCSPX-[0-9A-Za-z_-]+")]
    private static partial Regex SecretPattern();
}
