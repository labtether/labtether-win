namespace LabTetherAgent.Settings;

/// <summary>
/// Wrapper around Windows Credential Manager (PasswordVault) for secret storage.
/// On non-Windows or when PasswordVault is unavailable, falls back to file-based storage.
///
/// Resource names follow the pattern "LabTether:{SecretName}".
/// </summary>
public class CredentialStore
{
    public const string ApiTokenResource = "LabTether:ApiToken";
    public const string EnrollmentTokenResource = "LabTether:EnrollmentToken";
    public const string LocalApiAuthResource = "LabTether:LocalApiAuth";
    public const string WebRtcTurnPassResource = "LabTether:WebRTCTurnPass";

    private const string UserName = "LabTetherAgent";

    // PasswordVault is Windows-only and requires WinRT interop.
    // The actual implementation uses Windows.Security.Credentials.PasswordVault
    // which can only compile on Windows with the Windows App SDK.
    // This class provides the interface; platform-specific implementation
    // is wired in at build time.

    private readonly Dictionary<string, string> _fallbackStore = new();
    private readonly string? _fallbackPath;

    public CredentialStore()
    {
        var settingsDir = AgentSettings.GetSettingsDirectory();
        _fallbackPath = Path.Combine(settingsDir, ".credentials");
        LoadFallback();
    }

    public void Store(string resourceName, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Remove(resourceName);
            return;
        }

        // TODO: Replace with PasswordVault when compiling on Windows
        _fallbackStore[resourceName] = value;
        SaveFallback();
    }

    public string? Retrieve(string resourceName)
    {
        // TODO: Replace with PasswordVault when compiling on Windows
        return _fallbackStore.TryGetValue(resourceName, out var value) ? value : null;
    }

    public void Remove(string resourceName)
    {
        // TODO: Replace with PasswordVault when compiling on Windows
        _fallbackStore.Remove(resourceName);
        SaveFallback();
    }

    public void RemoveAll()
    {
        _fallbackStore.Clear();
        SaveFallback();
    }

    /// <summary>
    /// Load secrets into an AgentSettings instance.
    /// </summary>
    public void LoadInto(AgentSettings settings)
    {
        settings.ApiToken = Retrieve(ApiTokenResource) ?? string.Empty;
        settings.EnrollmentToken = Retrieve(EnrollmentTokenResource) ?? string.Empty;
        settings.LocalApiAuthToken = Retrieve(LocalApiAuthResource) ?? string.Empty;
        settings.WebRtcTurnPass = Retrieve(WebRtcTurnPassResource) ?? string.Empty;
    }

    /// <summary>
    /// Save secrets from an AgentSettings instance.
    /// </summary>
    public void SaveFrom(AgentSettings settings)
    {
        Store(ApiTokenResource, settings.ApiToken);
        Store(EnrollmentTokenResource, settings.EnrollmentToken);
        Store(LocalApiAuthResource, settings.LocalApiAuthToken);
        Store(WebRtcTurnPassResource, settings.WebRtcTurnPass);
    }

    private void LoadFallback()
    {
        if (_fallbackPath == null || !File.Exists(_fallbackPath))
            return;

        foreach (var line in File.ReadAllLines(_fallbackPath))
        {
            var sep = line.IndexOf('=');
            if (sep > 0)
                _fallbackStore[line[..sep]] = line[(sep + 1)..];
        }
    }

    private void SaveFallback()
    {
        if (_fallbackPath == null)
            return;

        var dir = Path.GetDirectoryName(_fallbackPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var lines = _fallbackStore.Select(kv => $"{kv.Key}={kv.Value}");
        File.WriteAllLines(_fallbackPath, lines);
    }
}
