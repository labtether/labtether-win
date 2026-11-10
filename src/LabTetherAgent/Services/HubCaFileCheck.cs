using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>What a Hub said about a CA file chosen under Advanced.</summary>
public enum HubCaFileVerdict
{
    /// <summary>The file alone verified the Hub's certificate.</summary>
    Verified,

    /// <summary>The Hub answered, and its certificate is not signed by this file.</summary>
    DoesNotMatch,

    /// <summary>The Hub could not be asked.</summary>
    NotChecked,
}

/// <summary>
/// A CA file chosen by hand becomes the agent's only root for the Hub once
/// the app has seen this file alone verify the Hub's certificate: the same
/// verified connection Trust this Hub makes. Until then the agent keeps its
/// earlier trust (that file and Windows' own list), so an update of the app
/// never takes a working device offline.
/// </summary>
internal static class HubCaFileCheck
{
    public const string DoesNotMatch = "This CA certificate does not match this Hub's certificate.";
    public const string HubNotReached =
        "The Hub could not be reached to check this CA certificate. Nothing was saved.";
    public const string NowOnlyRoot = "The agent now trusts only the chosen CA certificate file for this Hub.";
    public const string StartDoesNotMatch =
        "The chosen CA certificate file does not match this Hub's certificate. " +
        "The agent still also accepts certificates that Windows trusts.";
    public const string StartNotChecked =
        "The Hub could not be reached to check the chosen CA certificate file. " +
        "The check runs again at the next start.";

    /// <summary>True for a CA file the user chose, with the certificate check on and a TLS Hub address.</summary>
    public static bool Applies(string? hubUrl, bool tlsSkipVerify, string? caFile, bool managedCaFile) =>
        !managedCaFile && !tlsSkipVerify && !string.IsNullOrWhiteSpace(caFile) &&
        SettingsValidator.DeriveApiBaseUrl(hubUrl)?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the saved CA file is one the app has not yet checked against the Hub.</summary>
    public static bool IsPending(AgentSettings settings) =>
        Applies(settings.HubUrl, settings.TlsSkipVerify, settings.TlsCaFile, settings.UsesManagedCaFile) &&
        !settings.UsesVerifiedCaFile;

    /// <summary>One connection in which the file's roots are the only roots; nothing secret is sent.</summary>
    public static async Task<HubCaFileVerdict> RunAsync(ConnectionTester tester, string hubUrl, string caFile)
    {
        var result = await tester.TestAsync(hubUrl, tlsSkipVerify: false, tlsCaFile: caFile);
        if (result.Success)
            return HubCaFileVerdict.Verified;
        return result.CertificateRejected ? HubCaFileVerdict.DoesNotMatch : HubCaFileVerdict.NotChecked;
    }
}
