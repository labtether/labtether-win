using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Runtime.InteropServices;

namespace LabTetherAgent.Settings;

/// <summary>One application's persistence and installed OS-integration boundary.</summary>
public sealed class AppStateLocation
{
    public const string DirectoryArgument = "--state-directory";
    private const string SharedDirectoryMessage =
        "The state directory must belong to you, and other users must have no access to it. Choose a new folder; the app creates it with private permissions.";
    public static AppStateLocation Installed { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabTether"), false);

    public string DirectoryPath { get; }
    public bool IsIsolated { get; }
    public bool SupportsOsIntegration => !IsIsolated;
    public string Identity { get; }
    public string ActivationKey => IsIsolated ? "LabTetherAgent:" + Identity : "LabTetherAgent";
    public string SettingsPath => FilePath("settings.json");
    public string CredentialsPath => FilePath(".credentials");

    private AppStateLocation(string directory, bool isolated)
    {
        DirectoryPath = CanonicalDirectory(directory);
        IsIsolated = isolated;
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DirectoryPath.ToUpperInvariant())));
    }

    private static string CanonicalDirectory(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!OperatingSystem.IsWindows()) return full;
        var missing = new Stack<string>();
        var existing = new DirectoryInfo(full);
        while (!existing.Exists && existing.Parent != null)
        {
            missing.Push(existing.Name);
            existing = existing.Parent;
        }
        var buffer = new StringBuilder(32768);
        var length = GetLongPathName(existing.FullName, buffer, (uint)buffer.Capacity);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("Cannot resolve the state directory identity.");
        var canonical = buffer.ToString();
        while (missing.TryPop(out var part)) canonical = Path.Combine(canonical, part);
        return Path.TrimEndingDirectorySeparator(canonical);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);

    public static AppStateLocation FromArguments(IReadOnlyList<string> arguments)
    {
        string? directory = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith(DirectoryArgument + "=", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Pass the state directory as a separate argument.");
            if (!string.Equals(arguments[i], DirectoryArgument, StringComparison.OrdinalIgnoreCase))
                continue;
            if (directory != null || ++i >= arguments.Count || string.IsNullOrWhiteSpace(arguments[i]))
                throw new ArgumentException("Provide --state-directory once, with an absolute local directory.");
            directory = arguments[i];
        }
        return directory == null ? Installed : ForDirectory(directory);
    }

    public static AppStateLocation ForDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) ||
            directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal) ||
            directory.IndexOf(':', 2) >= 0)
            throw new ArgumentException("State directory must be an absolute local path.");
        var location = new AppStateLocation(directory, true);
        if (string.Equals(location.DirectoryPath, Path.GetPathRoot(location.DirectoryPath), StringComparison.OrdinalIgnoreCase) ||
            ContainsPath(Installed.DirectoryPath, location.DirectoryPath) ||
            ContainsPath(location.DirectoryPath, Installed.DirectoryPath))
            throw new ArgumentException("State directory must be separate from the installed application's state.");
        foreach (var part in location.DirectoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (part.EndsWith(' ') || part.EndsWith('.'))
                throw new ArgumentException("State directory cannot contain aliased path components.");
        // Create it now, privately, before any write could create it with
        // permissions inherited from a shared parent such as C:\.
        location.EnsureDirectory();
        return location;
    }

    public void Validate()
    {
        // Reject aliases at every existing ancestor, including junctions. A
        // caller cannot name the installed state through a redirected path.
        for (var current = new DirectoryInfo(DirectoryPath); current != null; current = current.Parent)
        {
            if (File.Exists(current.FullName) ||
                (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("State directory contains a redirected or non-directory path.");
        }
        if (IsIsolated) RequirePrivateDirectory();
    }

    public void EnsureDirectory()
    {
        Validate();
        if (IsIsolated && !System.IO.Directory.Exists(DirectoryPath))
            CreatePrivateDirectory();
        else
            System.IO.Directory.CreateDirectory(DirectoryPath);
        Validate();
    }

    /// <summary>
    /// A separate state folder can be anywhere, including places where every
    /// user may create folders. If another user owns it or can open it, they
    /// could plant settings.json (the Hub URL) or files the agent core trusts.
    /// </summary>
    private void RequirePrivateDirectory()
    {
        var directory = new DirectoryInfo(DirectoryPath);
        if (!directory.Exists) return;
        DirectorySecurity security;
        try { security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner); }
        catch (UnauthorizedAccessException) { throw new IOException(SharedDirectoryMessage); }
        var trusted = TrustedIdentities();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner))
            throw new IOException(SharedDirectoryMessage);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier identity ||
                !(trusted.Contains(identity) || identity.IsWellKnown(WellKnownSidType.CreatorOwnerSid)))
                throw new IOException(SharedDirectoryMessage);
        }
    }

    private void CreatePrivateDirectory()
    {
        var parent = Path.GetDirectoryName(DirectoryPath)
            ?? throw new IOException("State directory has no parent directory.");
        System.IO.Directory.CreateDirectory(parent);
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not resolve the current Windows identity.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var identity in TrustedIdentities())
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        // Does nothing if the folder appeared meanwhile; Validate then checks it.
        new DirectoryInfo(DirectoryPath).Create(security);
    }

    private static HashSet<SecurityIdentifier> TrustedIdentities() =>
    [
        WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not resolve the current Windows identity."),
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
    ];

    public string FilePath(string name)
    {
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name is "." or "..")
            throw new ArgumentException("State file name must be a single component.");
        Validate();
        var path = Path.Combine(DirectoryPath, name);
        if (File.Exists(path) || System.IO.Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("State file must be a regular, non-redirected file.");
        }
        return path;
    }

    internal bool SameOwner(AppStateLocation other) =>
        string.Equals(DirectoryPath, other.DirectoryPath, StringComparison.OrdinalIgnoreCase) && IsIsolated == other.IsIsolated;

    private static bool ContainsPath(string parent, string child) =>
        string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
