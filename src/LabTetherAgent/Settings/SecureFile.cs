using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LabTetherAgent.Settings;

/// <summary>Atomic file writes with a protected, current-user-only Windows DACL.</summary>
internal static class SecureFile
{
    public static void WriteAllText(string path, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            WriteAllBytes(path, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static void WriteAllBytes(string path, byte[] value)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Secure file path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // Protect the empty staging file before any secret bytes are
            // written, avoiding even a short inherited-ACL exposure window.
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
            }
            ApplyCurrentUserOnlyAcl(temporaryPath);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Truncate,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(value);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static void ApplyCurrentUserOnlyAcl(string path)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not resolve the current Windows identity.");
        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControl(security, currentUser);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new FileInfo(path).SetAccessControl(security);
    }

    public static void DeleteIfExists(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Refusing to delete a non-regular secret file.");
        }
        File.Delete(path);
    }

    /// <summary>
    /// Return true only for a non-empty, non-reparse-point secret file whose
    /// DACL has inheritance disabled. Files produced by this type also grant
    /// access only to the current user, SYSTEM, and Administrators; checking
    /// the protected DACL here prevents inherited or redirected files from
    /// being treated as durable authentication state.
    /// </summary>
    public static bool IsPrivateRegularFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 ||
                (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return false;
            }

            var security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            if (!security.AreAccessRulesProtected)
                return false;

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
                return false;

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                currentUser.Value,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
            };
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
                || !allowed.Contains(owner.Value))
            {
                return false;
            }

            var currentUserCanRead = false;
            var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    continue;
                if (rule.IdentityReference is not SecurityIdentifier identity
                    || !allowed.Contains(identity.Value))
                {
                    return false;
                }

                if (identity.Equals(currentUser)
                    && (rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.FullControl)) != 0)
                {
                    currentUserCanRead = true;
                }
            }

            return currentUserCanRead;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void AddFullControl(FileSecurity security, SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
