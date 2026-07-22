using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class ConnectionTesterTests
{
    [Fact]
    public void DefaultHandlerRefusesRedirectsAndOnlySkipsTlsWhenRequested()
    {
        using var strict = Assert.IsType<HttpClientHandler>(ConnectionTester.CreateDefaultHandler(false));
        using var tlsSkip = Assert.IsType<HttpClientHandler>(ConnectionTester.CreateDefaultHandler(true));

        Assert.False(strict.AllowAutoRedirect);
        Assert.Null(strict.ServerCertificateCustomValidationCallback);
        Assert.False(tlsSkip.AllowAutoRedirect);
        Assert.NotNull(tlsSkip.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void CustomCaHandlerTrustsTheChosenRootButStillRejectsNameMismatch()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=hub.example.test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature,
            true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var caPath = Path.Combine(Path.GetTempPath(), $"labtether-ca-{Guid.NewGuid():N}.pem");

        try
        {
            File.WriteAllText(caPath, certificate.ExportCertificatePem());
            using var handler = Assert.IsType<HttpClientHandler>(
                ConnectionTester.CreateDefaultHandler(false, caPath));
            var callback = Assert.IsType<
                Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>>(
                handler.ServerCertificateCustomValidationCallback);

            Assert.True(callback(
                new HttpRequestMessage(),
                certificate,
                null,
                SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(callback(
                new HttpRequestMessage(),
                certificate,
                null,
                SslPolicyErrors.RemoteCertificateNameMismatch
                    | SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            File.Delete(caPath);
        }
    }

    [Fact]
    public async Task TestAsyncRejectsMissingCustomCaBeforeNetworkDispatch()
    {
        var dispatchCount = 0;
        var tester = new ConnectionTester(_ =>
        {
            dispatchCount++;
            return new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        });
        var missing = Path.Combine(Path.GetTempPath(), $"missing-ca-{Guid.NewGuid():N}.pem");

        var result = await tester.TestAsync("https://hub.example.test", false, missing);

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, dispatchCount);
    }

    [Fact]
    public async Task TestAsyncRejectsConflictingTlsTrustOptionsBeforeNetworkDispatch()
    {
        var dispatchCount = 0;
        var tester = new ConnectionTester((_, _) =>
        {
            dispatchCount++;
            return new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        });

        var result = await tester.TestAsync(
            "https://hub.example.test",
            tlsSkipVerify: true,
            tlsCaFile: @"C:\LabTether\ca.pem");

        Assert.False(result.Success);
        Assert.Contains("not both", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, dispatchCount);
    }

    [Fact]
    public async Task TestAsyncAcceptsOnlyCanonicalHubIdentity()
    {
        var tester = CreateTester(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("{\"service\":\"labtether-hub\",\"message\":\"running\"}")
        });

        var result = await tester.TestAsync("https://hub.example.test/ws/agent");

        Assert.True(result.Success);
        Assert.Equal("Verified LabTether hub.", result.Message);
    }

    [Fact]
    public async Task TestAsyncProbesOnlyCanonicalHubRoot()
    {
        Uri? requestedUri = null;
        var tester = CreateTester(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("{\"service\":\"labtether-hub\"}")
            };
        });

        var result = await tester.TestAsync("wss://hub.example.test:8443/custom/agent/path");

        Assert.True(result.Success);
        Assert.Equal(new Uri("https://hub.example.test:8443/"), requestedUri);
    }

    [Theory]
    [InlineData("wss://user:secret@hub.example.test/ws/agent")]
    [InlineData("wss://hub.example.test/ws/agent?token=secret")]
    [InlineData("wss://hub.example.test/ws/agent#fragment")]
    [InlineData("ftp://hub.example.test/ws/agent")]
    public async Task TestAsyncRejectsUnsafeOrUnsupportedHubUrls(string hubUrl)
    {
        var dispatchCount = 0;
        var tester = CreateTester(_ =>
        {
            dispatchCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await tester.TestAsync(hubUrl);

        Assert.False(result.Success);
        Assert.Equal(0, dispatchCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task TestAsyncRejectsEveryNon200Response(HttpStatusCode status)
    {
        var tester = CreateTester(_ => new HttpResponseMessage(status)
        {
            Content = JsonContent("{\"service\":\"labtether-hub\"}")
        });

        var result = await tester.TestAsync("https://hub.example.test");

        Assert.False(result.Success);
        Assert.Contains($"HTTP {(int)status}", result.Message);
    }

    [Theory]
    [InlineData("{\"service\":\"something-else\"}")]
    [InlineData("{\"status\":\"ok\"}")]
    [InlineData("not-json")]
    public async Task TestAsyncRejectsUnrelatedOrMalformedEndpoint(string body)
    {
        var tester = CreateTester(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(body)
        });

        var result = await tester.TestAsync("https://hub.example.test");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task TestAsyncRejectsOversizedResponseBeforeParsing()
    {
        var tester = CreateTester(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new string('x', 8 * 1024 + 1))
        });

        var result = await tester.TestAsync("https://hub.example.test");

        Assert.False(result.Success);
        Assert.Contains("too large", result.Message);
    }

    private static ConnectionTester CreateTester(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return new ConnectionTester(_ => new StubHandler(responseFactory));
    }

    private static StringContent JsonContent(string body)
    {
        return new StringContent(body, Encoding.UTF8, "application/json");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
