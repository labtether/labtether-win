using System.Diagnostics;
#if WINDOWS
using Windows.Security.Credentials;
#endif

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

    private const string VaultResource = "LabTether";
    private const string UserName = "LabTetherAgent";

    private readonly bool _vaultAvailable;

    // Fallback store for when PasswordVault is not available
    private readonly Dictionary<string, string> _fallbackStore = new();
    private readonly string? _fallbackPath;

    public CredentialStore()
    {
        _vaultAvailable = ProbeVault();

        if (!_vaultAvailable)
        {
            Trace.TraceWarning(
                "CredentialStore: Windows PasswordVault is not available. " +
                "Falling back to file-based credential storage. " +
                "Secrets will NOT be protected by the OS credential manager.");

            var settingsDir = AgentSettings.GetSettingsDirectory();
            _fallbackPath = Path.Combine(settingsDir, ".credentials");
            LoadFallback();
        }
    }

    public void Store(string resourceName, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Remove(resourceName);
            return;
        }

        if (_vaultAvailable)
        {
            VaultStore(resourceName, value);
        }
        else
        {
            _fallbackStore[resourceName] = value;
            SaveFallback();
        }
    }

    public string? Retrieve(string resourceName)
    {
        if (_vaultAvailable)
        {
            return VaultRetrieve(resourceName);
        }

        return _fallbackStore.TryGetValue(resourceName, out var value) ? value : null;
    }

    public void Remove(string resourceName)
    {
        if (_vaultAvailable)
        {
            VaultRemove(resourceName);
        }
        else
        {
            _fallbackStore.Remove(resourceName);
            SaveFallback();
        }
    }

    public void RemoveAll()
    {
        if (_vaultAvailable)
        {
            VaultRemove(ApiTokenResource);
            VaultRemove(EnrollmentTokenResource);
            VaultRemove(LocalApiAuthResource);
            VaultRemove(WebRtcTurnPassResource);
        }
        else
        {
            _fallbackStore.Clear();
            SaveFallback();
        }
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

    // ── PasswordVault operations ────────────────────────────────────────

    /// <summary>
    /// Returns true if PasswordVault can be instantiated on this platform.
    /// </summary>
    private static bool ProbeVault()
    {
#if WINDOWS
        try
        {
            _ = new PasswordVault();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"CredentialStore: PasswordVault probe failed: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private static void VaultStore(string resourceName, string value)
    {
#if WINDOWS
        var vault = new PasswordVault();

        // Remove any existing credential for this resource first
        try
        {
            var existing = vault.Retrieve(VaultResource, resourceName);
            vault.Remove(existing);
        }
        catch
        {
            // No existing credential — that's fine
        }

        vault.Add(new PasswordCredential(VaultResource, resourceName, value));
#endif
    }

    private static string? VaultRetrieve(string resourceName)
    {
#if WINDOWS
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(VaultResource, resourceName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            // Credential not found
            return null;
        }
#else
        return null;
#endif
    }

    private static void VaultRemove(string resourceName)
    {
#if WINDOWS
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(VaultResource, resourceName);
            vault.Remove(credential);
        }
        catch
        {
            // Credential not found — nothing to remove
        }
#endif
    }

    // ── Fallback file-based storage ─────────────────────────────────────

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
