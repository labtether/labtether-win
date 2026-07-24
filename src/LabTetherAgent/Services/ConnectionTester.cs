using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>
/// Tests connectivity to a LabTether hub URL.
/// </summary>
public class ConnectionTester
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const int MaxProbeBodyBytes = 8 * 1024;
    private readonly Func<bool, string?, HttpMessageHandler> _handlerFactory;

    public ConnectionTester() : this(CreateDefaultHandler)
    {
    }

    public ConnectionTester(Func<bool, HttpMessageHandler> handlerFactory)
        : this((skipVerify, _) => handlerFactory(skipVerify))
    {
    }

    internal ConnectionTester(Func<bool, string?, HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
    }

    /// <summary>
    /// Test if the hub URL reaches LabTether's public discovery endpoint.
    /// </summary>
    /// <param name="hubUrl">Hub URL to probe.</param>
    /// <param name="tlsSkipVerify">
    /// When true, accept any server certificate (homelab self-signed mode). Must be
    /// the user's explicit <c>AgentSettings.TlsSkipVerify</c> value — do not pass
    /// <c>true</c> unconditionally.
    /// </param>
    public async Task<ConnectionTestResult> TestAsync(
        string hubUrl,
        bool tlsSkipVerify = false,
        string? tlsCaFile = null)
    {
        if (string.IsNullOrWhiteSpace(hubUrl))
            return new ConnectionTestResult(false, "Hub URL is empty.");

        // Reuse the strict settings parser so user information, queries,
        // fragments, and unsupported schemes cannot leak into the probe.
        var identityUrl = HubIdentityUrl(hubUrl);
        if (identityUrl is null)
            return new ConnectionTestResult(false, "Invalid URL format.");
        if (HasConflictingTlsTrustOptions(tlsSkipVerify, tlsCaFile))
        {
            return new ConnectionTestResult(
                false,
                "Choose either a custom CA certificate or skip certificate verification, not both.");
        }
        if (!TryNormalizeCustomCaFile(tlsCaFile, out var normalizedCaFile))
            return new ConnectionTestResult(false, "Custom CA certificate file is unavailable.");
        tlsCaFile = normalizedCaFile.Length == 0 ? null : normalizedCaFile;
        if (tlsCaFile != null && !IsUsableCustomCaFile(tlsCaFile))
            return new ConnectionTestResult(false, "Custom CA certificate file is unavailable.");

        try
        {
            using var handler = _handlerFactory(tlsSkipVerify, tlsCaFile);
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var timeout = new CancellationTokenSource(Timeout);
            using var response = await client.GetAsync(
                identityUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new ConnectionTestResult(
                    false,
                    $"Hub verification failed (HTTP {(int)response.StatusCode}).");
            }

            if (response.Content.Headers.ContentLength is > MaxProbeBodyBytes)
                return new ConnectionTestResult(false, "Hub verification response is too large.");

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaxProbeBodyBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), timeout.Token);
                if (read == 0)
                    break;
                total += read;
            }

            if (total > MaxProbeBodyBytes)
                return new ConnectionTestResult(false, "Hub verification response is too large.");

            try
            {
                using var document = JsonDocument.Parse(buffer.AsMemory(0, total));
                if (!IsCanonicalHubIdentity(document.RootElement))
                {
                    return new ConnectionTestResult(false, "The endpoint is not a LabTether hub.");
                }
            }
            catch (JsonException)
            {
                return new ConnectionTestResult(false, "The endpoint returned an invalid hub response.");
            }

            return new ConnectionTestResult(true, "Verified LabTether hub.");
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, "Connection timed out.");
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestResult(false, "Connection failed.");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new ConnectionTestResult(false, "Custom CA certificate is invalid.");
        }
        catch (Exception)
        {
            return new ConnectionTestResult(false, "Unexpected connection error.");
        }
    }

    internal static Uri? HubIdentityUrl(string hubUrl)
    {
        var apiBase = SettingsValidator.DeriveApiBaseUrl(hubUrl);
        if (!Uri.TryCreate(apiBase?.TrimEnd('/') + "/api/v1/discover", UriKind.Absolute, out var identityUrl))
            return null;
        return identityUrl;
    }

    internal static bool IsCanonicalHubIdentity(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return false;

        // Older direct API origins identify themselves at their root with this
        // shape. Retain compatibility while all unified console origins use
        // the public discovery contract below.
        if (payload.TryGetProperty("service", out var service) &&
            service.ValueKind == JsonValueKind.String &&
            string.Equals(service.GetString(), "labtether-hub", StringComparison.Ordinal))
        {
            return true;
        }

        if (!payload.TryGetProperty("hub", out var hub) ||
            hub.ValueKind != JsonValueKind.String ||
            !string.Equals(hub.GetString(), "labtether", StringComparison.Ordinal) ||
            !payload.TryGetProperty("hub_ws_url", out var hubWsUrl) ||
            hubWsUrl.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("enroll_url", out var enrollUrl) ||
            enrollUrl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var wsUrl = hubWsUrl.GetString();
        var normalizedWsUrl = SettingsValidator.NormalizeHubWebSocketUrl(wsUrl);
        if (normalizedWsUrl is null ||
            !string.Equals(normalizedWsUrl, wsUrl, StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(enrollUrl.GetString(), UriKind.Absolute, out var enrollmentUri) &&
            enrollmentUri.Scheme is "http" or "https" &&
            string.IsNullOrEmpty(enrollmentUri.UserInfo) &&
            string.IsNullOrEmpty(enrollmentUri.Query) &&
            string.IsNullOrEmpty(enrollmentUri.Fragment) &&
            string.Equals(enrollmentUri.AbsolutePath, "/api/v1/enroll", StringComparison.Ordinal);
    }

    internal static bool IsUsableCustomCaFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                && info.Length > 0
                && (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsValidCustomCaFile(string path)
    {
        if (!IsUsableCustomCaFile(path))
            return false;

        try
        {
            using var handler = CreateDefaultHandler(tlsSkipVerify: false, tlsCaFile: path);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
                                   or IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException)
        {
            return false;
        }
    }

    internal static bool HasConflictingTlsTrustOptions(bool tlsSkipVerify, string? tlsCaFile) =>
        tlsSkipVerify && !string.IsNullOrWhiteSpace(tlsCaFile);

    internal static bool TryNormalizeCustomCaFile(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        try
        {
            normalized = Path.GetFullPath(value.Trim());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static HttpMessageHandler CreateDefaultHandler(bool tlsSkipVerify, string? tlsCaFile = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        if (tlsSkipVerify)
        {
            // Opt-in via AgentSettings.TlsSkipVerify — homelab self-signed mode.
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        else if (!string.IsNullOrWhiteSpace(tlsCaFile))
        {
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPemFile(tlsCaFile);
            var roots = certificates
                .Where(certificate => certificate.SubjectName.RawData.AsSpan().SequenceEqual(
                    certificate.IssuerName.RawData))
                .ToArray();
            if (roots.Length == 0)
                throw new System.Security.Cryptography.CryptographicException(
                    "The custom CA bundle does not contain a self-signed root certificate.");

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, sslErrors) =>
            {
                if (certificate == null
                    || (sslErrors & (SslPolicyErrors.RemoteCertificateNameMismatch
                        | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                chain.ChainPolicy.ExtraStore.AddRange(certificates);
                return chain.Build(certificate);
            };
        }
        return handler;
    }
}

public record ConnectionTestResult(bool Success, string Message);
