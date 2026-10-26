using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace LabTetherAgent.Settings;

/// <summary>One application's persistence and installed OS-integration boundary.</summary>
public sealed class AppStateLocation
{
    public const string DirectoryArgument = "--state-directory";
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
        location.Validate();
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
    }

    public void EnsureDirectory()
    {
        Validate();
        System.IO.Directory.CreateDirectory(DirectoryPath);
        Validate();
    }

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
