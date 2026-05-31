using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabTetherAgent.Services;

/// <summary>
/// Checks for tray app updates by comparing the current version
/// against the latest release on GitHub or a configured update URL.
/// </summary>
public class UpdateChecker
{
    internal const string DefaultUpdateUrl = "https://api.github.com/repos/labtether/labtether-win/releases/latest";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

    private readonly string _currentVersion;
    private readonly string _updateUrl;
    private DateTime _lastCheck = DateTime.MinValue;

    public event Action<string>? OnUpdateAvailable; // new version string

    public UpdateChecker(string currentVersion, string updateUrl = "")
    {
        _currentVersion = currentVersion.Trim();
        _updateUrl = string.IsNullOrWhiteSpace(updateUrl)
            ? DefaultUpdateUrl
            : updateUrl.Trim();
    }

    /// <summary>
    /// Check for updates if enough time has elapsed since the last check.
    /// </summary>
    public async Task CheckIfDueAsync()
    {
        if (DateTime.UtcNow - _lastCheck < CheckInterval)
            return;

        await CheckAsync();
    }

    /// <summary>
    /// Force an update check now.
    /// </summary>
    public async Task CheckAsync()
    {
        _lastCheck = DateTime.UtcNow;

        try
        {
            using var client = new HttpClient { Timeout = HttpTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LabTetherAgent/1.0");

            using var response = await client.GetAsync(_updateUrl);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var latestVersion = ExtractLatestVersion(json);
            if (latestVersion == null) return;

            if (IsNewerVersion(latestVersion, _currentVersion))
            {
                OnUpdateAvailable?.Invoke(latestVersion);
            }
        }
        catch
        {
            // Silently ignore update check failures
        }
    }

    /// <summary>
    /// Compare two semver-ish version strings.
    /// Returns true if candidate is newer than current.
    /// </summary>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        if (Version.TryParse(NormalizeVersion(candidate), out var candidateVer) &&
            Version.TryParse(NormalizeVersion(current), out var currentVer))
        {
            return candidateVer > currentVer;
        }
        return false;
    }

    internal static string? ExtractLatestVersion(string json)
    {
        var release = JsonSerializer.Deserialize<GitHubRelease>(json);
        return release?.TagName?.TrimStart('v');
    }

    private static string NormalizeVersion(string v)
    {
        v = v.TrimStart('v');
        // Ensure at least major.minor format
        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => v
        };
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
