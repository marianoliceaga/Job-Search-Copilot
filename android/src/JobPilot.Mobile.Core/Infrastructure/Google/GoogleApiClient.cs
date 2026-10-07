using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <summary>
/// Authenticated JSON calls to Google APIs, mapping failures to Spanish messages instead of exceptions. A rejected token
/// (401) is dropped from Android's cache and the call is retried once with a new one.
/// </summary>
public abstract class GoogleApiClient(HttpClient http, GoogleAuthService auth)
{
    protected static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    protected async Task<GoogleResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, string requiredScope, CancellationToken cancellationToken)
    {
        if (!auth.Status().GrantedScopes.Contains(requiredScope))
        {
            return GoogleResult<T>.Fail("No diste ese permiso al conectar Google. Volvé a conectar y aceptalo (Configuración → Google).");
        }

        for (var attempt = 1; ; attempt++)
        {
            var token = await auth.GetAccessTokenAsync(cancellationToken);
            if (token.Value is not { } accessToken)
            {
                return GoogleResult<T>.Fail(token.Error!);
            }

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    await auth.ForgetTokenAsync(accessToken);
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    if (typeof(T) == typeof(bool))
                    {
                        return GoogleResult<T>.Ok((T)(object)true);
                    }

                    var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                    return value is null ? GoogleResult<T>.Fail("Google devolvió una respuesta vacía.") : GoogleResult<T>.Ok(value);
                }

                return GoogleResult<T>.Fail(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Google rechazó el permiso. Volvé a conectar (Configuración → Google).",
                    HttpStatusCode.Forbidden => "Google no permitió la operación: revisá que la API esté habilitada en tu proyecto y que hayas aceptado el permiso.",
                    HttpStatusCode.NotFound => NotFound,
                    HttpStatusCode.TooManyRequests => "Google limitó los pedidos por un rato. Probá de nuevo en unos minutos.",
                    _ => $"Google devolvió un error ({(int)response.StatusCode}).",
                });
            }
            catch (HttpRequestException)
            {
                return GoogleResult<T>.Fail("No se pudo conectar con Google.");
            }
            catch (JsonException)
            {
                return GoogleResult<T>.Fail("Google devolvió una respuesta ilegible.");
            }
        }
    }

    public const string NotFound = "No se encontró en Google (puede haber sido borrado).";
}
