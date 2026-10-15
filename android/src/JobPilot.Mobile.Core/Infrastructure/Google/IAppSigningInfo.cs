using System.Security.Cryptography;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <summary>
/// The installed app's package name and signing certificate, implemented by the host: the setup wizard shows them so the
/// owner can paste them into Google Cloud's "Android" OAuth client (a CI APK and a Google Play install have different ones).
/// </summary>
public interface IAppSigningInfo
{
    string PackageName { get; }

    /// <summary>DER-encoded signing certificate of the installed app, or null when it cannot be read.</summary>
    byte[]? SigningCertificate();
}

/// <summary>Used until the host registers its own (tests, previews): the package name only.</summary>
internal sealed class UnknownAppSigningInfo : IAppSigningInfo
{
    public string PackageName => SigningFingerprint.PackageName;

    public byte[]? SigningCertificate() => null;
}

public static class SigningFingerprint
{
    /// <summary>The app id (<c>ApplicationId</c> in the project); Google Cloud asks for it in the Android client.</summary>
    public const string PackageName = "com.vidi.jobpilot";

    /// <summary>SHA-1 of the certificate as Google Cloud and <c>keytool</c> show it: upper-case hex pairs separated by colons.</summary>
    public static string Sha1(byte[] certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return string.Join(':', SHA1.HashData(certificate).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
