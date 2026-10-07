using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using JobPilot.Mobile.Core.Infrastructure.Google;

namespace JobPilot.Mobile.Platforms.Android.Google;

/// <summary>
/// Google's authorization API for Android (Play services <c>AuthorizationClient</c>, play-services-auth 22.0.0), as Google
/// documents for apps that access Google APIs from the device: an "Android" OAuth client identified by package name and
/// signing certificate, a one-hour access token per authorization and no refresh token on the device.
/// </summary>
public sealed class AndroidGoogleAuthorizer : IGoogleAuthorizer
{
    private const int ResolutionRequestCode = 4711;
    private static TaskCompletionSource<(Result ResultCode, Intent? Data)>? _pendingResolution;

    public bool IsAvailable =>
        GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(global::Android.App.Application.Context) == ConnectionResult.Success;

    public async Task<GoogleAuthorization> AuthorizeAsync(IReadOnlyList<string> scopes, bool interactive, CancellationToken cancellationToken)
    {
        if (Platform.CurrentActivity is not { } activity)
        {
            return GoogleAuthorization.Failed("La app no está en primer plano; volvé a intentar.");
        }

        var client = Identity.GetAuthorizationClient(activity);
        var request = AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes(scopes.Select(s => new Scope(s)).ToList())
            .Build();
        try
        {
            var result = await client.Authorize(request).AsAsync<AuthorizationResult>().WaitAsync(cancellationToken);
            if (result.HasResolution)
            {
                if (!interactive)
                {
                    return GoogleAuthorization.Failed("Google pide volver a dar el permiso.", needsConsent: true);
                }

                result = await ResolveAsync(activity, client, result.PendingIntent!, cancellationToken);
                if (result is null)
                {
                    return GoogleAuthorization.Failed("Cancelaste el permiso en Google.");
                }
            }

            return result.AccessToken is { } token
                ? new GoogleAuthorization(token, result.GrantedScopes?.ToList() ?? [], false, null)
                : GoogleAuthorization.Failed("Google no devolvió un permiso de acceso.");
        }
        catch (ApiException ex)
        {
            return GoogleAuthorization.Failed(Message(ex.StatusCode));
        }
    }

    public async Task ClearTokenAsync(string accessToken)
    {
        if (Platform.CurrentActivity is not { } activity)
        {
            return;
        }

        try
        {
            var request = ClearTokenRequest.InvokeBuilder().SetToken(accessToken).Build();
            await Identity.GetAuthorizationClient(activity).ClearToken(request).AsAsync<Java.Lang.Object>();
        }
        catch (ApiException)
        {
            // The cache entry is gone or Play services is unavailable: the next authorization asks Google anyway.
        }
    }

    /// <summary>Called by <see cref="MainActivity"/> with the outcome of Google's consent screen.</summary>
    public static bool OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != ResolutionRequestCode)
        {
            return false;
        }

        Interlocked.Exchange(ref _pendingResolution, null)?.TrySetResult((resultCode, data));
        return true;
    }

    /// <summary>Shows Google's consent screen and reads its result; null when the owner cancelled.</summary>
    private static async Task<AuthorizationResult?> ResolveAsync(Activity activity, IAuthorizationClient client, PendingIntent pending, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(Result ResultCode, Intent? Data)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pendingResolution, completion)?.TrySetCanceled(cancellationToken);
        await MainThread.InvokeOnMainThreadAsync(() =>
            activity.StartIntentSenderForResult(pending.IntentSender, ResolutionRequestCode, null, 0, 0, 0));
        var (resultCode, data) = await completion.Task.WaitAsync(cancellationToken);
        return resultCode == Result.Ok && data is not null ? client.GetAuthorizationResultFromIntent(data) : null;
    }

    private static string Message(int statusCode) => statusCode switch
    {
        CommonStatusCodes.DeveloperError =>
            "Google no reconoce esta app. Revisá el cliente OAuth de tipo Android en Google Cloud: paquete com.vidi.jobpilot y la huella SHA-1 de la keystore que firma el APK.",
        CommonStatusCodes.NetworkError => "No se pudo conectar con Google.",
        CommonStatusCodes.Canceled => "Cancelaste el permiso en Google.",
        _ => $"Google no dio el permiso (código {statusCode}).",
    };
}
