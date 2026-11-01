using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

public class AppStateLocationTests
{
    [Fact]
    public async Task SelectedStateOwnsSettingsCredentialsSetupAndSecretCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        try
        {
            var a = AppStateLocation.FromArguments(["--state-directory", Path.Combine(root, "a")]);
            var b = AppStateLocation.FromArguments(["--state-directory", Path.Combine(root, "b")]);
            var first = AgentSettings.Load(a);
            var second = AgentSettings.Load(b);
            first.HubUrl = second.HubUrl = "https://state-owner.invalid";
            first.EnrollmentToken = new string('a', 32);
            first.GroupId = "first-group";
            second.ApiToken = new string('b', 32);
            var credentialsA = new CredentialStore(a);
            var credentialsB = new CredentialStore(b);
            first.Save();
            second.Save();
            credentialsA.SaveFrom(first);
            credentialsB.SaveFrom(second);
            AgentEnvironmentBuilder.BuildEnvironment(second, "8092", "second-auth");
            var untouched = Directory.GetFiles(b.DirectoryPath).ToDictionary(path => path, File.ReadAllBytes);

            var environment = AgentEnvironmentBuilder.BuildEnvironment(first, "8091", "first-auth");
            Assert.Equal(a.FilePath("agent-token"), environment["LABTETHER_TOKEN_FILE"]);
            SecureFile.WriteAllText(a.FilePath("agent-token"), "durable-first-token");
            Assert.True(first.ClearConsumedEnrollmentTokenPreservingAgentToken(credentialsA));
            Assert.True(first.ClearPersistedGroupIntentAfterEnrollment());
            first.CleanupEphemeralSecrets();
            var reloaded = AgentSettings.Load(a);
            new CredentialStore(a).LoadInto(reloaded);
            Assert.True(reloaded.HasPersistedAgentToken);
            Assert.Empty(reloaded.EnrollmentToken);
            Assert.Empty(reloaded.GroupId);
            Assert.False(File.Exists(a.FilePath("local-api-auth-token")));
            Assert.False(File.Exists(a.FilePath("enrollment-token")));

            using var owner = new AppState(first, credentialsA, new AgentProcess(), new LocalApiClient(), () => null);
            var beforeSetup = File.ReadAllBytes(a.SettingsPath);
            var failedSetup = await owner.ConnectAgentForSetupAsync(first.CloneForSetup(), true, CancellationToken.None);
            Assert.False(failedSetup.Success);
            Assert.Empty(Directory.GetDirectories(a.DirectoryPath, ".setup-*"));
            Assert.Equal(beforeSetup, File.ReadAllBytes(a.SettingsPath));
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ConnectAgentForSetupAsync(
                second.CloneForSetup(), true, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => credentialsA.SaveFrom(second));
            Assert.Throws<InvalidOperationException>(() => first.ApplyCommittedSetup(second));
            foreach (var (path, bytes) in untouched) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(untouched.Count, Directory.GetFiles(b.DirectoryPath).Length);
            Assert.NotEqual(a.ActivationKey, b.ActivationKey);
            var startup = new LoginItemManager(a);
            Assert.False(startup.IsEnabled());
            Assert.True(startup.SetEnabled(false));
            Assert.False(startup.SetEnabled(true));
            Assert.Throws<ArgumentException>(() => AppStateLocation.ForDirectory(AppStateLocation.Installed.DirectoryPath));
            Assert.Throws<ArgumentException>(() => AppStateLocation.FromArguments(["--state-directory="]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StateDirectoryOtherUsersCanWriteIsRefused()
    {
        // Risk: another user pre-creates the folder and plants settings.json
        // with their own Hub URL, or files the agent core trusts.
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared");
        try
        {
            var info = Directory.CreateDirectory(shared);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
            Assert.Throws<IOException>(() => AppStateLocation.ForDirectory(shared));

            // A folder the app creates for itself passes the same check.
            var created = AppStateLocation.ForDirectory(Path.Combine(root, "created"));
            Assert.True(Directory.Exists(created.DirectoryPath));
            created.Validate();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryStopsOnlyItsRecordedChildWhenBinaryIsShared()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var a = AppStateLocation.ForDirectory(Path.Combine(root, "a"));
        var b = AppStateLocation.ForDirectory(Path.Combine(root, "b"));
        var binary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        using var first = Child(a);
        using var second = Child(b);
        string? firstStartupError = null;
        string? secondStartupError = null;
        first.OnError += message => firstStartupError = message;
        second.OnError += message => secondStartupError = message;
        try
        {
            first.Start(binary, new Dictionary<string, string>());
            second.Start(binary, new Dictionary<string, string>());
            Assert.True(first.IsRunning, firstStartupError);
            Assert.True(second.IsRunning, secondStartupError);
            var secondRecord = File.ReadAllBytes(b.FilePath("child-process.json"));
            using var restartedOwner = new AgentProcess(a);
            restartedOwner.KillOrphanedAgents(binary);
            first.RefreshExitedChild();
            second.RefreshExitedChild();
            Assert.False(first.IsRunning);
            Assert.True(second.IsRunning);
            Assert.False(File.Exists(a.FilePath("child-process.json")));
            Assert.Equal(secondRecord, File.ReadAllBytes(b.FilePath("child-process.json")));

            RetireDeadRecordAfterMove(a, binary, Path.Combine(root, "moved", "labtether-agent.exe"));
            second.RefreshExitedChild();
            Assert.True(second.IsRunning);
            Assert.Equal(secondRecord, File.ReadAllBytes(b.FilePath("child-process.json")));
        }
        finally
        {
            await first.StopAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(10));
            await second.StopAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(10));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void RetireDeadRecordAfterMove(AppStateLocation location, string binary, string movedBinary)
    {
        // Use the real record owner without an AgentProcess exit callback that could
        // erase the stale record before recovery observes it.
        using var child = new System.Diagnostics.Process
        {
            StartInfo = new ProcessStartInfo(binary)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }
            }
        };
        Assert.True(child.Start());
        try
        {
            new AgentChildOwnership(location).Remember(child);
            child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(5000));
            Assert.True(File.Exists(location.FilePath("child-process.json")));
            using var restartedOwner = new AgentProcess(location);
            restartedOwner.KillOrphanedAgents(movedBinary);
            Assert.False(File.Exists(location.FilePath("child-process.json")));
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(5000));
        }
    }

    private static AgentProcess Child(AppStateLocation location) => new(location, info =>
    {
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 30");
        return new System.Diagnostics.Process { StartInfo = info };
    });
}
