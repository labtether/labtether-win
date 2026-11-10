using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

// "Trust this Hub" asks a person to compare a fingerprint and then trusts a CA
// read over an unverified connection. A wrong fingerprint, an offer for a
// certificate that the CA did not issue, or an offer for an expired or
// wrong-host certificate would each let another server pass as the Hub.
public class HubCertificateTrustTests
{
    // A throwaway CA; the fingerprint is `openssl x509 -outform DER | sha256sum`.
    private const string KnownCaPem = """
        -----BEGIN CERTIFICATE-----
        MIIBrDCCAVGgAwIBAgIUdyAO4oIhrkg+oxRE1NO9lP9RTn0wCgYIKoZIzj0EAwIw
        KzEQMA4GA1UECgwHRXhhbXBsZTEXMBUGA1UEAwwORXhhbXBsZSBIdWIgQ0EwHhcN
        MjYxMTEwMDYxMzM3WhcNMzYxMTA3MDYxMzM3WjArMRAwDgYDVQQKDAdFeGFtcGxl
        MRcwFQYDVQQDDA5FeGFtcGxlIEh1YiBDQTBZMBMGByqGSM49AgEGCCqGSM49AwEH
        A0IABEZPmoaU5anAqXBT+Zly2mzJLCjbJNVtwrFUReqX6p9oJrMQhQxTzopliWMn
        s5FLROaOmqTV2Hoypz39s9mizUqjUzBRMB0GA1UdDgQWBBRd+S+2RVg1BzTTDFg1
        9gqLJDNTKjAfBgNVHSMEGDAWgBRd+S+2RVg1BzTTDFg19gqLJDNTKjAPBgNVHRMB
        Af8EBTADAQH/MAoGCCqGSM49BAMCA0kAMEYCIQD8cUZxZcK+yceziw6MDtniMFrT
        Oo1D4m81W+yoHyROSAIhANuG+9G++WZoKfW5ONUMVJn3nzoRaMCUxS0NOCznxMSc
        -----END CERTIFICATE-----
        """;
    private const string KnownFingerprint = "fd44a8fbb823d34a35f0d72ced949251c15896094d449781c9600eec2de80b32";

    [Fact]
    public void FingerprintIsSha256OfTheCaDerInGroupsOfFour()
    {
        Assert.Equal(KnownFingerprint, HubCertificateTrust.FingerprintOfPem(KnownCaPem));
        Assert.Equal(
            "fd44 a8fb b823 d34a 35f0 d72c ed94 9251 c158 9609 4d44 9781 c960 0eec 2de8 0b32",
            HubCertificateTrust.Group(KnownFingerprint));
    }

    [Fact]
    public void OffersTheHubCaOnlyWhenTheHubCertificateChainsToItAndMatchesTheHost()
    {
        var now = DateTimeOffset.UtcNow;
        using var ca = TestCertificates.Ca("Example Hub CA", now);
        using var otherCa = TestCertificates.Ca("Another CA", now);
        using var hub = TestCertificates.Leaf(ca, "hub.example.test", now.AddDays(-1), now.AddDays(30));
        using var expired = TestCertificates.Leaf(ca, "hub.example.test", now.AddDays(-30), now.AddDays(-1));
        var none = new X509Certificate2Collection();

        var offer = HubCertificateTrust.Evaluate(hub, none, ca.ExportCertificatePem(), "hub.example.test", now);
        Assert.Null(offer.Problem);
        Assert.Equal(HubCertificateTrust.Fingerprint(ca), offer.Offer!.Fingerprint);

        // Signed by the Hub's CA but offered another CA: never an offer.
        Assert.Equal(HubCertificateTrust.NotSignedByOfferedCa,
            HubCertificateTrust.Evaluate(hub, none, otherCa.ExportCertificatePem(), "hub.example.test", now).Problem);
        // A wrong host name or an expired certificate stays an error.
        Assert.Equal(HubCertificateTrust.WrongHost("nas01.example.test"),
            HubCertificateTrust.Evaluate(hub, none, ca.ExportCertificatePem(), "nas01.example.test", now).Problem);
        Assert.Equal(HubCertificateTrust.Expired,
            HubCertificateTrust.Evaluate(expired, none, ca.ExportCertificatePem(), "hub.example.test", now).Problem);
        // No CA, text that is no certificate, or the Hub's own leaf offered as a CA.
        foreach (var notACa in new[] { null, "not found", hub.ExportCertificatePem() })
        {
            var refused = HubCertificateTrust.Evaluate(hub, none, notACa, "hub.example.test", now);
            Assert.Null(refused.Offer);
            Assert.Equal(HubCertificateTrust.NoCaOffered, refused.Problem);
        }
    }

    // The CA is fetched without certificate verification, so that request must
    // carry nothing a stranger could use: no token, cookie or credential.
    [Fact]
    public async Task ProbeSendsNothingSecretAndNeverUsesPlainHttp()
    {
        var requests = new List<HttpRequestMessage>();
        var probe = new HubCertificateProbe(_ => new RecordingHandler(requests));

        var decision = await probe.ProbeAsync("wss://hub.example.test:8443/ws/agent");

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://hub.example.test:8443/api/v1/ca.crt", request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
        // The stub served no TLS certificate, so there is nothing to offer.
        Assert.Null(decision.Offer);

        Assert.Null((await probe.ProbeAsync("http://hub.example.test:8080")).Offer);
        Assert.Single(requests);
    }

    // A shown certificate is not proof: anyone can present the Hub's public
    // certificate. Without a finished handshake (an answered request) there
    // is no offer, however well the shown certificate matches the offered CA.
    [Fact]
    public async Task CertificateThatIsOnlyShownGivesNoOffer()
    {
        var now = DateTimeOffset.UtcNow;
        using var ca = TestCertificates.Ca("Example Hub CA", now);
        using var hub = TestCertificates.Leaf(ca, "hub.example.test", now.AddDays(-1), now.AddDays(30));
        var caPem = ca.ExportCertificatePem();

        var shownOnly = new HubCertificateProbe(observe => new TlsStubHandler(() =>
        {
            observe(hub, null, SslPolicyErrors.RemoteCertificateChainErrors);
            throw new HttpRequestException("handshake did not finish", new AuthenticationException());
        }));
        Assert.Null((await shownOnly.ProbeAsync("https://hub.example.test:8443")).Offer);

        // The same certificate over a finished handshake is offered for the question.
        var finished = new HubCertificateProbe(observe => new TlsStubHandler(() =>
        {
            observe(hub, null, SslPolicyErrors.RemoteCertificateChainErrors);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(caPem) };
        }));
        Assert.Equal(HubCertificateTrust.Fingerprint(ca),
            (await finished.ProbeAsync("https://hub.example.test:8443")).Offer!.Fingerprint);
    }

    // After Trust this Hub only the accepted CA counts. A server Windows itself
    // trusts (a publicly trusted certificate) is not the Hub and must fail.
    [Fact]
    public void AcceptedCaIsTheOnlyRootEvenWhenWindowsTrustsTheServer()
    {
        var now = DateTimeOffset.UtcNow;
        using var ca = TestCertificates.Ca("Example Hub CA", now);
        using var publicCa = TestCertificates.Ca("A Public CA", now);
        using var hub = TestCertificates.Leaf(ca, "hub.example.test", now.AddDays(-1), now.AddDays(30));
        using var other = TestCertificates.Leaf(publicCa, "hub.example.test", now.AddDays(-1), now.AddDays(30));
        using var handler = (HttpClientHandler)ConnectionTester.CreateDefaultHandler(
            tlsSkipVerify: false, tlsCaFile: null, tlsCaPem: ca.ExportCertificatePem());
        var verify = handler.ServerCertificateCustomValidationCallback!;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://hub.example.test:8443");

        Assert.True(verify(request, hub, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(verify(request, other, null, SslPolicyErrors.None));
    }

    private sealed class TlsStubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(KnownCaPem) });
        }
    }
}

/// <summary>Short-lived certificates for trust tests; nothing here is a real Hub's.</summary>
internal static class TestCertificates
{
    public static X509Certificate2 Ca(string name, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}, O=Example", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(now.AddDays(-60), now.AddDays(365));
    }

    public static X509Certificate2 Leaf(X509Certificate2 ca, string host, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(12));
    }
}
