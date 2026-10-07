namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <param name="AccessToken">Short-lived token (about one hour) for the granted scopes; null on failure.</param>
/// <param name="GrantedScopes">Scopes Google granted (the owner may leave some out).</param>
/// <param name="NeedsConsent">The owner has to approve the scopes on Google's screen (only shown when interactive).</param>
/// <param name="Error">User-facing (Spanish) reason when there is no token.</param>
public sealed record GoogleAuthorization(string? AccessToken, IReadOnlyList<string> GrantedScopes, bool NeedsConsent, string? Error)
{
    public static GoogleAuthorization Failed(string error, bool needsConsent = false) => new(null, [], needsConsent, error);
}

/// <summary>
/// Android's authorization API (Google Play services <c>AuthorizationClient</c>), implemented by the host. No refresh
/// token exists on the phone: every session asks again, silently while the owner keeps the grant.
/// </summary>
public interface IGoogleAuthorizer
{
    /// <summary>Google Play services is present and usable on this phone.</summary>
    bool IsAvailable { get; }

    /// <param name="interactive">Show Google's consent screen if needed; otherwise return <see cref="GoogleAuthorization.NeedsConsent"/>.</param>
    Task<GoogleAuthorization> AuthorizeAsync(IReadOnlyList<string> scopes, bool interactive, CancellationToken cancellationToken);

    /// <summary>Removes a token Google rejected from the device cache, so the next authorization gets a new one.</summary>
    Task ClearTokenAsync(string accessToken);
}
