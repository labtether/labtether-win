using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>
/// Reads the CA a Hub offers after Windows rejected the Hub's certificate, so
/// setup can ask the user to trust it. The one request here is made without
/// certificate verification, so it carries nothing secret: no token, cookie or
/// Windows credential, only a GET for the public /api/v1/ca.crt file. What it
/// reads is trusted only after the user compares the fingerprint.
/// </summary>
public class HubCertificateProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const int MaxCaBytes = 64 * 1024;
    private readonly Func<Action<X509Certificate2, X509Chain?, SslPolicyErrors>, HttpMessageHandler> _handlerFactory;

    public HubCertificateProbe() : this(CreateUnverifiedHandler)
    {
    }

    internal HubCertificateProbe(
        Func<Action<X509Certificate2, X509Chain?, SslPolicyErrors>, HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
    }

    public async Task<HubTrustDecision> ProbeAsync(string hubUrl, CancellationToken cancellationToken = default)
    {
        var apiBase = SettingsValidator.DeriveApiBaseUrl(hubUrl);
        if (!Uri.TryCreate(apiBase?.TrimEnd('/') + "/api/v1/ca.crt", UriKind.Absolute, out var caUrl) ||
            caUrl.Scheme != Uri.UriSchemeHttps)
        {
            return HubTrustDecision.Refused(HubCertificateTrust.NotTrusted);
        }

        X509Certificate2? hubCertificate = null;
        var presented = new X509Certificate2Collection();
        var windowsTrustsIt = false;
        try
        {
            using var handler = _handlerFactory((certificate, chain, errors) =>
            {
                // The callback's objects are disposed after the handshake; keep copies.
                hubCertificate ??= X509CertificateLoader.LoadCertificate(certificate.RawData);
                if (chain != null)
                {
                    foreach (var extra in chain.ChainPolicy.ExtraStore)
                        presented.Add(X509CertificateLoader.LoadCertificate(extra.RawData));
                }
                windowsTrustsIt = errors == SslPolicyErrors.None;
            });
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, caUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (hubCertificate == null)
                return HubTrustDecision.Refused(HubCertificateTrust.NotTrusted);
            // Windows trusts this certificate, so the failure came from the CA
            // file the user entered. There is nothing of the Hub's own to trust.
            if (windowsTrustsIt)
                return HubTrustDecision.Refused(HubCertificateTrust.CaFileDoesNotMatch);

            var caPem = response.StatusCode == HttpStatusCode.OK
                ? await ReadBoundedTextAsync(response, timeout.Token)
                : null;
            return HubCertificateTrust.Evaluate(hubCertificate, presented, caPem, caUrl.IdnHost, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException
                                   or System.Security.Cryptography.CryptographicException)
        {
            return HubTrustDecision.Refused(HubCertificateTrust.NotTrusted);
        }
        finally
        {
            hubCertificate?.Dispose();
            foreach (var certificate in presented)
                certificate.Dispose();
        }
    }

    private static async Task<string?> ReadBoundedTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxCaBytes)
            return null;
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxCaBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        return total > MaxCaBytes ? null : Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static HttpMessageHandler CreateUnverifiedHandler(
        Action<X509Certificate2, X509Chain?, SslPolicyErrors> observe) => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
        {
            if (certificate == null)
                return false;
            observe(certificate, chain, errors);
            return true;
        },
    };
}
