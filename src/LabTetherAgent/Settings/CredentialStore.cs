using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private static readonly byte[] FallbackHeader = "LTDPAPI1\n"u8.ToArray();
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

    public CredentialStore() : this(vaultAvailable: null, fallbackPath: null)
    {
    }

    internal CredentialStore(bool? vaultAvailable, string? fallbackPath)
    {
        _vaultAvailable = vaultAvailable ?? ProbeVault();

        if (!_vaultAvailable)
        {
            Trace.TraceWarning(
                "CredentialStore: Windows PasswordVault is not available. " +
                "Falling back to a current-user DPAPI-protected credential file.");

            _fallbackPath = fallbackPath
                ?? Path.Combine(AgentSettings.GetSettingsDirectory(), ".credentials");
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
        // Local API credentials are process-scoped and must never survive an
        // app restart. Remove any value written by older builds.
        Remove(LocalApiAuthResource);
        settings.LocalApiAuthToken = string.Empty;
        settings.WebRtcTurnPass = Retrieve(WebRtcTurnPassResource) ?? string.Empty;
    }

    /// <summary>
    /// Save secrets from an AgentSettings instance.
    /// </summary>
    public void SaveFrom(AgentSettings settings)
    {
        Store(ApiTokenResource, settings.ApiToken);
        Store(EnrollmentTokenResource, settings.EnrollmentToken);
        Remove(LocalApiAuthResource);
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

        try
        {
            var payload = File.ReadAllBytes(_fallbackPath);
            if (payload.AsSpan().StartsWith(FallbackHeader))
            {
                var protectedPayload = payload.AsSpan(FallbackHeader.Length).ToArray();
                var plaintext = ProtectedData.Unprotect(
                    protectedPayload,
                    optionalEntropy: FallbackHeader,
                    scope: DataProtectionScope.CurrentUser);
                try
                {
                    var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext);
                    if (stored != null)
                    {
                        foreach (var (key, value) in stored)
                            _fallbackStore[key] = value;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(protectedPayload);
                }
                return;
            }

            // One-time migration from older plaintext fallback files. The
            // migrated data is immediately replaced atomically with DPAPI data.
            foreach (var line in Encoding.UTF8.GetString(payload).Split('\n'))
            {
                var sep = line.IndexOf('=');
                if (sep > 0)
                    _fallbackStore[line[..sep]] = line[(sep + 1)..].TrimEnd('\r');
            }
            SaveFallback();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"CredentialStore: failed to decrypt fallback credentials: {ex.Message}");
            _fallbackStore.Clear();
        }
    }

    private void SaveFallback()
    {
        if (_fallbackPath == null)
            return;

        var dir = Path.GetDirectoryName(_fallbackPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(_fallbackStore);
        try
        {
            var protectedPayload = ProtectedData.Protect(
                plaintext,
                optionalEntropy: FallbackHeader,
                scope: DataProtectionScope.CurrentUser);
            var output = new byte[FallbackHeader.Length + protectedPayload.Length];
            Buffer.BlockCopy(FallbackHeader, 0, output, 0, FallbackHeader.Length);
            Buffer.BlockCopy(protectedPayload, 0, output, FallbackHeader.Length, protectedPayload.Length);
            SecureFile.WriteAllBytes(_fallbackPath, output);
            CryptographicOperations.ZeroMemory(protectedPayload);
            CryptographicOperations.ZeroMemory(output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
