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
    public async Task<ConnectionTestResult> TestAsync(string hubUrl)
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
            // Allow self-signed certs for homelab environments
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            using var client = new HttpClient(handler) { Timeout = Timeout };
            var response = await client.GetAsync(httpUrl);

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
