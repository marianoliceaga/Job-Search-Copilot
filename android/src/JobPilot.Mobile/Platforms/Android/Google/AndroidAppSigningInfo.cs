using Android.Content.PM;
using JobPilot.Mobile.Core.Infrastructure.Google;

namespace JobPilot.Mobile.Platforms.Android.Google;

/// <summary>
/// Reads the installed app's signing certificate from the package manager, so the Google setup wizard shows the SHA-1 that
/// the "Android" OAuth client needs: the upload key's for a CI APK, Google's app signing key's for an install from Play.
/// </summary>
public sealed class AndroidAppSigningInfo : IAppSigningInfo
{
    public string PackageName => global::Android.App.Application.Context.PackageName ?? SigningFingerprint.PackageName;

    public byte[]? SigningCertificate()
    {
        try
        {
            var manager = global::Android.App.Application.Context.PackageManager;
            if (manager is null)
            {
                return null;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var info = manager.GetPackageInfo(PackageName, PackageManager.PackageInfoFlags.Of((long)PackageInfoFlags.SigningCertificates));
                return CurrentSigner(info.SigningInfo);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
#pragma warning disable CA1422 // Replaced by the PackageInfoFlags overload on API 33, used above.
                var info = manager.GetPackageInfo(PackageName, PackageInfoFlags.SigningCertificates);
#pragma warning restore CA1422
                return CurrentSigner(info?.SigningInfo);
            }

#pragma warning disable CS0618, CA1422 // Signatures is the only way before API 28 (minimum API is 26).
            var legacy = manager.GetPackageInfo(PackageName, PackageInfoFlags.Signatures);
            return legacy?.Signatures?.FirstOrDefault()?.ToByteArray();
#pragma warning restore CS0618, CA1422
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private static byte[]? CurrentSigner(SigningInfo? signing)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28) || signing is null)
        {
            return null;
        }

        // The certificate Android verifies the APK with now (the last one if the key was rotated).
        var signers = signing.HasMultipleSigners ? signing.GetApkContentsSigners() : signing.GetSigningCertificateHistory();
        return signers is { Length: > 0 } ? (signing.HasMultipleSigners ? signers[0] : signers[^1]).ToByteArray() : null;
    }
}
