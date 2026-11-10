using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>What a Hub said about a CA file chosen under Advanced.</summary>
public enum HubCaFileVerdict
{
    /// <summary>The file alone verified the Hub's certificate.</summary>
    Verified,

    /// <summary>The Hub answered, and its certificate is not signed by this file.</summary>
    DoesNotMatch,

    /// <summary>The Hub could not be asked, or the file could not be read.</summary>
    NotChecked,
}

/// <summary>
/// A CA file chosen by hand becomes the agent's only root for the Hub once
/// the app has seen this file alone verify the Hub's certificate: the same
/// verified connection Trust this Hub makes. Its fingerprint is then recorded
/// beside the enrollment, and from that moment the choice is kept: a later
/// check that fails, a Hub that cannot be reached or a file that is gone
/// never brings Windows' own certificate list back.
/// Only a file that was never checked (saved by a version before this rule)
/// keeps the earlier trust, that file and Windows' list, until it is checked.
/// </summary>
internal static class HubCaFileCheck
{
    public const string DoesNotMatch = "This CA certificate does not match this Hub's certificate.";
    public const string HubNotReached =
        "The Hub could not be reached to check this CA certificate. Nothing was saved.";
    public const string FileChanged =
        "This Hub's CA certificate file has changed. Put the right file back, or choose Re-enroll in the tray menu.";
    public const string NowOnlyRoot = "The agent now trusts only the chosen CA certificate file for this Hub.";
    public const string StartDoesNotMatch =
        "The chosen CA certificate file does not match this Hub's certificate. " +
        "The agent still also accepts certificates that Windows trusts. Open Settings to remove or replace the file.";
    public const string StartNotChecked =
        "The chosen CA certificate file could not be checked with the Hub. " +
        "The check runs again when the Hub is reached and at the next start.";

    /// <summary>True for a CA file the user chose, with the certificate check on and a TLS Hub address.</summary>
    public static bool Applies(string? hubUrl, bool tlsSkipVerify, string? caFile, bool managedCaFile) =>
        !managedCaFile && !tlsSkipVerify && !string.IsNullOrWhiteSpace(caFile) &&
        SettingsValidator.DeriveApiBaseUrl(hubUrl)?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True while the saved CA file is one the app has never seen verify the Hub.</summary>
    public static bool IsPending(AgentSettings settings) =>
        settings.EnrolledHubCaFingerprint.Length == 0 &&
        Applies(settings.HubUrl, settings.TlsSkipVerify, settings.TlsCaFile, settings.UsesManagedCaFile);

    /// <summary>
    /// The fingerprint recorded for a CA file: for one certificate its SHA-256,
    /// as every LabTether screen shows it; for a bundle one SHA-256 over its
    /// certificates' fingerprints in file order. Empty when the file cannot be
    /// read or holds no certificate.
    /// </summary>
    public static string Fingerprint(string? caFile)
    {
        if (string.IsNullOrWhiteSpace(caFile))
            return string.Empty;
        var certificates = new X509Certificate2Collection();
        try
        {
            certificates.ImportFromPemFile(caFile.Trim());
            if (certificates.Count == 0)
                return string.Empty;
            if (certificates.Count == 1)
                return HubCertificateTrust.Fingerprint(certificates[0]);
            var all = string.Concat(certificates.Select(HubCertificateTrust.Fingerprint));
            return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(all))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
        finally
        {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    /// <summary>One connection in which the file's roots are the only roots; nothing secret is sent.</summary>
    public static async Task<HubCaFileVerdict> RunAsync(ConnectionTester tester, string hubUrl, string caFile)
    {
        var result = await tester.TestAsync(hubUrl, tlsSkipVerify: false, tlsCaFile: caFile);
        if (result.Success)
            return HubCaFileVerdict.Verified;
        return result.CertificateRejected ? HubCaFileVerdict.DoesNotMatch : HubCaFileVerdict.NotChecked;
    }
}
