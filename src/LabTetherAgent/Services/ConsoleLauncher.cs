using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>
/// Opens the web console for the saved Hub. The Hub URL from Add device names
/// the Hub API port, whose "/" answers with JSON that names the console in
/// <c>console_url</c>; opening the Hub URL itself would show that JSON. Any
/// other answer means the Hub URL already is the console's address.
/// </summary>
public sealed class ConsoleLauncher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int MaxBodyBytes = 8 * 1024;
    private readonly Func<bool, string?, HttpMessageHandler> _handlerFactory;

    public ConsoleLauncher() : this(ConnectionTester.CreateDefaultHandler)
    {
    }

    internal ConsoleLauncher(Func<bool, string?, HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
    }

    public async Task OpenAsync(AgentSettings settings)
    {
        var url = await ResolveAsync(settings.HubUrl, settings.TlsSkipVerify, settings.TlsCaFile);
        if (url != null)
            Launch(url);
    }

    /// <summary>
    /// Returns the console address for <paramref name="hubUrl"/>, or the Hub's
    /// HTTP base when the Hub does not name a console. Uses the same TLS trust
    /// as the connection test, so a pinned CA or skip-verify setting applies.
    /// </summary>
    internal async Task<string?> ResolveAsync(string? hubUrl, bool tlsSkipVerify, string? tlsCaFile)
    {
        var apiBase = SettingsValidator.DeriveApiBaseUrl(hubUrl);
        if (apiBase == null)
            return null;
        if (ConnectionTester.HasConflictingTlsTrustOptions(tlsSkipVerify, tlsCaFile))
            return apiBase;

        try
        {
            var caFile = string.IsNullOrWhiteSpace(tlsCaFile) ? null : tlsCaFile.Trim();
            using var handler = _handlerFactory(tlsSkipVerify, caFile);
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var timeout = new CancellationTokenSource(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, apiBase + "/");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength is > MaxBodyBytes)
            {
                return apiBase;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaxBodyBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length &&
                   (read = await body.ReadAsync(buffer.AsMemory(total), timeout.Token)) > 0)
            {
                total += read;
            }

            if (total > MaxBodyBytes)
                return apiBase;
            return ConsoleUrlFromHubRoot(buffer.AsMemory(0, total)) ?? apiBase;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                   or IOException or UnauthorizedAccessException or ArgumentException
                                   or System.Security.Cryptography.CryptographicException)
        {
            return apiBase;
        }
    }

    /// <summary>
    /// Reads <c>console_url</c> from the Hub API root answer. Only an http or
    /// https address without user information is accepted, because the result
    /// is handed to the shell.
    /// </summary>
    internal static string? ConsoleUrlFromHubRoot(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("service", out var service) ||
                service.ValueKind != JsonValueKind.String ||
                !string.Equals(service.GetString(), "labtether-hub", StringComparison.Ordinal) ||
                !root.TryGetProperty("console_url", out var consoleUrl) ||
                consoleUrl.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(consoleUrl.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Launch(string url)
    {
        try
        {
            // Fully qualified: inside LabTetherAgent, "Process" is the
            // LabTetherAgent.Process namespace.
            System.Diagnostics.Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or PlatformNotSupportedException)
        {
            Debug.WriteLine($"Failed to open URL '{url}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
