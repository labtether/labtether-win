using System.Security.Cryptography;

namespace LabTetherAgent.Settings;

/// <summary>
/// Owns the bundled Go child's durable files. Native wrappers must never fall
/// back to the machine-wide standalone-agent state under ProgramData.
/// </summary>
internal static class AgentManagedState
{
    internal const string AgentTokenFileName = "agent-token";
    internal const string EnrollmentStateFileName = "enrollment-state.json";
    internal const string AgentConfigFileName = "agent-config.json";
    internal const string DeviceKeyFileName = "device-key";
    internal const string DevicePublicKeyFileName = "device-key.pub";
    internal const string DeviceFingerprintFileName = "device-fingerprint";
    internal const string CaCertificateFileName = "ca.crt";

    private static readonly string[] ManagedArtifactNames =
    {
        AgentTokenFileName,
        EnrollmentStateFileName,
        AgentConfigFileName,
        DeviceKeyFileName,
        DevicePublicKeyFileName,
        DeviceFingerprintFileName,
        CaCertificateFileName,
    };

    internal static IReadOnlyList<string> SnapshotArtifactNames => ManagedArtifactNames;

    internal static bool IsSetupStateReady(string directory) =>
        SecureFile.IsPrivateRegularFile(Path.Combine(directory, AgentTokenFileName)) &&
        SecureFile.IsPrivateRegularFile(Path.Combine(directory, DeviceKeyFileName));

    internal static Dictionary<string, byte[]> CaptureSetupArtifacts(string stagingDirectory)
    {
        if (!IsSetupStateReady(stagingDirectory))
        {
            throw new InvalidOperationException(
                "The staged setup did not produce protected durable credentials and device identity.");
        }

        var captured = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var fileName in ManagedArtifactNames)
            {
                var path = Path.Combine(stagingDirectory, fileName);
                if (!File.Exists(path))
                    continue;

                captured[fileName] = ReadBoundedRegularFile(path, MaximumBytes(fileName));
            }
            return captured;
        }
        catch
        {
            ZeroArtifacts(captured);
            throw;
        }
    }

    internal static void CommitArtifacts(
        string destinationDirectory,
        IReadOnlyDictionary<string, byte[]> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (!artifacts.ContainsKey(AgentTokenFileName) ||
            !artifacts.ContainsKey(DeviceKeyFileName))
        {
            throw new InvalidOperationException(
                "Committed setup state requires an agent token and device key.");
        }

        var allowed = new HashSet<string>(ManagedArtifactNames, StringComparer.OrdinalIgnoreCase);
        if (artifacts.Keys.Any(fileName => !allowed.Contains(fileName)))
            throw new InvalidOperationException("Setup state contained an unknown managed artifact.");

        Directory.CreateDirectory(destinationDirectory);
        foreach (var fileName in ManagedArtifactNames)
            SecureFile.DeleteIfExists(Path.Combine(destinationDirectory, fileName));

        foreach (var (fileName, contents) in artifacts)
        {
            if (contents.Length == 0 || contents.Length > MaximumBytes(fileName))
                throw new InvalidOperationException($"Managed artifact '{fileName}' has an invalid size.");
            SecureFile.WriteAllBytes(Path.Combine(destinationDirectory, fileName), contents);
        }
    }

    internal static bool MigrateLegacyDeviceIdentityIfNeeded(
        string destinationDirectory,
        string? legacyDirectory = null)
    {
        var destinationKey = Path.Combine(destinationDirectory, DeviceKeyFileName);
        if (File.Exists(destinationKey))
            return false;

        // Only an enrollment-state file proves that the existing wrapper token
        // was issued to the legacy device key. API-token installs must create a
        // new wrapper-owned identity instead of borrowing a service identity.
        if (!SecureFile.IsPrivateRegularFile(
                Path.Combine(destinationDirectory, AgentTokenFileName)) ||
            !File.Exists(Path.Combine(destinationDirectory, EnrollmentStateFileName)))
        {
            return false;
        }

        legacyDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LabTether");
        var legacyKey = Path.Combine(legacyDirectory, DeviceKeyFileName);
        if (!File.Exists(legacyKey))
            return false;

        var migrated = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var fileName in new[]
                     {
                         DeviceKeyFileName,
                         DevicePublicKeyFileName,
                         DeviceFingerprintFileName,
                     })
            {
                var source = Path.Combine(legacyDirectory, fileName);
                if (!File.Exists(source))
                    continue;
                migrated[fileName] = ReadBoundedRegularFile(source, MaximumBytes(fileName));
            }

            if (!migrated.ContainsKey(DeviceKeyFileName))
                return false;

            foreach (var (fileName, contents) in migrated)
                SecureFile.WriteAllBytes(Path.Combine(destinationDirectory, fileName), contents);
            return true;
        }
        finally
        {
            ZeroArtifacts(migrated);
        }
    }

    internal static void ZeroArtifacts(IReadOnlyDictionary<string, byte[]> artifacts)
    {
        foreach (var contents in artifacts.Values)
            CryptographicOperations.ZeroMemory(contents);
    }

    private static byte[] ReadBoundedRegularFile(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes ||
            (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException($"Refusing invalid managed state file '{info.Name}'.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan);
        if (stream.Length != info.Length)
            throw new IOException($"Managed state file '{info.Name}' changed while opening.");

        var expectedLength = (int)stream.Length;
        var contents = new byte[expectedLength];
        stream.ReadExactly(contents);
        if (stream.Position != expectedLength || stream.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(contents);
            throw new IOException($"Managed state file '{info.Name}' changed while reading.");
        }
        return contents;
    }

    private static int MaximumBytes(string fileName) => fileName switch
    {
        CaCertificateFileName => 1024 * 1024,
        AgentTokenFileName or EnrollmentStateFileName or AgentConfigFileName => 64 * 1024,
        DeviceKeyFileName or DevicePublicKeyFileName or DeviceFingerprintFileName => 4 * 1024,
        _ => throw new InvalidOperationException("Unknown managed artifact."),
    };
}
