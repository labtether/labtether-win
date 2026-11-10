using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LabTetherAgent.Services;

/// <summary>A Hub CA that setup may ask the user to trust.</summary>
/// <param name="CaPem">The CA certificate as PEM, re-encoded from what was read.</param>
/// <param name="Fingerprint">SHA-256 of the CA's DER bytes, 64 lowercase hex characters.</param>
public sealed record HubTrustOffer(string CaPem, string Fingerprint)
{
    /// <summary>The fingerprint in groups of 4, as every LabTether screen shows it.</summary>
    public string GroupedFingerprint => HubCertificateTrust.Group(Fingerprint);
}

/// <summary>Either an offer to trust the Hub's own CA, or the reason there is none.</summary>
public sealed record HubTrustDecision(HubTrustOffer? Offer, string? Problem)
{
    public static HubTrustDecision Refused(string problem) => new(null, problem);
}

/// <summary>
/// Decides whether a Hub certificate that Windows does not trust may be offered
/// for trust. Setup offers it only when the one thing missing is trust in the
/// Hub's own CA: the certificate must be in date, match the host name and chain
/// to the CA the Hub offers. Anything else is an error and offers no trust.
/// </summary>
public static class HubCertificateTrust
{
    public const string NotTrusted = "This Hub's certificate isn't trusted.";
    public const string NoCaOffered =
        "This Hub's certificate isn't trusted, and the Hub offers no CA certificate of its own. " +
        "Enter the Hub's CA certificate file under Advanced.";
    public const string NotSignedByOfferedCa =
        "This Hub's certificate isn't signed by the CA certificate the Hub offers. " +
        "Enter the Hub's CA certificate file under Advanced.";
    public const string CaFileDoesNotMatch =
        "The CA certificate file under Advanced doesn't match this Hub's certificate.";
    public const string TrustedCaDidNotVerify =
        "This Hub could not be verified with the certificate you trusted.";
    public const string Expired = "This Hub's certificate has expired.";
    public const string NotValidYet = "This Hub's certificate is not valid yet.";

    public static string WrongHost(string host) =>
        $"This Hub's certificate is for a different address, not {host}.";

    /// <summary>SHA-256 of the certificate's DER bytes, as the Hub reports in ca_fingerprint_sha256.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    /// <summary>"fd44a8fb…" as "fd44 a8fb …".</summary>
    public static string Group(string fingerprint) =>
        string.Join(' ', fingerprint.Chunk(4).Select(part => new string(part)));

    /// <summary>The fingerprint of the first certificate in a PEM file, or null when it has none.</summary>
    public static string? FingerprintOfPem(string? pem)
    {
        using var certificate = SingleSelfSignedCertificate(pem);
        return certificate == null ? null : Fingerprint(certificate);
    }

    /// <param name="hubCertificate">The certificate the Hub presented.</param>
    /// <param name="presented">Other certificates the Hub sent with it.</param>
    /// <param name="caPem">The CA the Hub offers at /api/v1/ca.crt.</param>
    /// <param name="host">The host name or address the user typed.</param>
    public static HubTrustDecision Evaluate(
        X509Certificate2 hubCertificate,
        X509Certificate2Collection presented,
        string? caPem,
        string host,
        DateTimeOffset now)
    {
        // A wrong address or an expired certificate is said as that, whether
        // or not the Hub has a CA to offer; neither can be trusted away.
        if (!MatchesHost(hubCertificate, host))
            return HubTrustDecision.Refused(WrongHost(host));
        if (now.UtcDateTime > hubCertificate.NotAfter.ToUniversalTime())
            return HubTrustDecision.Refused(Expired);
        if (now.UtcDateTime < hubCertificate.NotBefore.ToUniversalTime())
            return HubTrustDecision.Refused(NotValidYet);
        using var ca = SingleSelfSignedCertificate(caPem);
        if (ca == null)
            return HubTrustDecision.Refused(NoCaOffered);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.ExtraStore.AddRange(presented);
        if (!chain.Build(hubCertificate))
        {
            var expired = chain.ChainStatus.Any(status => status.Status.HasFlag(X509ChainStatusFlags.NotTimeValid));
            return HubTrustDecision.Refused(expired ? Expired : NotSignedByOfferedCa);
        }

        return new HubTrustDecision(new HubTrustOffer(ca.ExportCertificatePem(), Fingerprint(ca)), null);
    }

    private static bool MatchesHost(X509Certificate2 certificate, string host)
    {
        try
        {
            return certificate.MatchesHostname(host);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // The Hub serves exactly one CA certificate. A bundle, a leaf or text that
    // is not a certificate is not something to ask a person to trust.
    private static X509Certificate2? SingleSelfSignedCertificate(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            return null;
        var certificates = new X509Certificate2Collection();
        try
        {
            certificates.ImportFromPem(pem);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var selfSigned = certificates.Count == 1 &&
            certificates[0].SubjectName.RawData.AsSpan().SequenceEqual(certificates[0].IssuerName.RawData);
        if (selfSigned)
            return certificates[0];
        foreach (var certificate in certificates)
            certificate.Dispose();
        return null;
    }
}
