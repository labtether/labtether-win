using System.Text.Json;

namespace LabTetherAgent.Services;

/// <summary>
/// Checks for tray app updates by comparing the current version
/// against the latest release on GitHub or a configured update URL.
/// </summary>
public class UpdateChecker
{
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
            ? "https://api.github.com/repos/labtether/win-agent/releases/latest"
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

            var response = await client.GetAsync(_updateUrl);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release?.TagName == null) return;

            var latestVersion = release.TagName.TrimStart('v');
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
        public string? TagName { get; set; }
    }
}
