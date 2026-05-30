namespace LabTetherAgent.Services;

/// <summary>
/// Tests connectivity to a LabTether hub URL.
/// </summary>
public class ConnectionTester
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Test if the hub URL is reachable via HTTPS.
    /// </summary>
    /// <param name="hubUrl">Hub URL to probe.</param>
    /// <param name="tlsSkipVerify">
    /// When true, accept any server certificate (homelab self-signed mode). Must be
    /// the user's explicit <c>AgentSettings.TlsSkipVerify</c> value — do not pass
    /// <c>true</c> unconditionally.
    /// </param>
    public async Task<ConnectionTestResult> TestAsync(string hubUrl, bool tlsSkipVerify = false)
    {
        if (string.IsNullOrWhiteSpace(hubUrl))
            return new ConnectionTestResult(false, "Hub URL is empty.");

        // Convert ws(s) to http(s) for the health check
        var httpUrl = hubUrl.Trim()
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);

        // Strip /ws/agent path — we're testing the base URL
        if (httpUrl.EndsWith("/ws/agent", StringComparison.OrdinalIgnoreCase))
            httpUrl = httpUrl[..^"/ws/agent".Length];

        if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out _))
            return new ConnectionTestResult(false, "Invalid URL format.");

        try
        {
            using var handler = new HttpClientHandler();
            if (tlsSkipVerify)
            {
                // Opt-in via AgentSettings.TlsSkipVerify — homelab self-signed mode.
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var response = await client.GetAsync(httpUrl);

            return new ConnectionTestResult(true, $"Connected (HTTP {(int)response.StatusCode})");
        }
        catch (TaskCanceledException)
        {
            return new ConnectionTestResult(false, "Connection timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new ConnectionTestResult(false, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Unexpected error: {ex.Message}");
        }
    }
}

public record ConnectionTestResult(bool Success, string Message);
