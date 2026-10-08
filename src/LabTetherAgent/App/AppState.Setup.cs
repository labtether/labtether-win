using LabTetherAgent.Api;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using LabTetherAgent.State;
using System.Net.NetworkInformation;
using System.Security.Cryptography;

namespace LabTetherAgent.App;

// Setup transaction and durable state rollback.
public partial class AppState
{
    /// <summary>
    /// Starts the Go child for setup and waits for real authenticated Hub
    /// connectivity. Enrollment setup additionally requires the one-use
    /// credential to be replaced by a durable agent token and removed from the
    /// wrapper store before success is reported.
    /// </summary>
    internal async Task<AgentConnectionAttemptResult> ConnectAgentForSetupAsync(
        AgentSettings candidate,
        bool requiresDurableEnrollment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_setupTransactionActive)
        {
            return AgentConnectionAttemptResult.Failed(
                "Another setup attempt is already running. Wait for it to finish and try again.");
        }

        _setupTransactionActive = true;
        var priorWasRunning = AgentProcess.IsRunning;
        var persistenceTouched = false;
        var stagingDirectory = Path.Combine(
            AgentSettings.GetSettingsDirectory(),
            $".setup-{Guid.NewGuid():N}");
        Dictionary<string, byte[]>? stagedArtifacts = null;

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using var snapshot = SetupPersistenceSnapshot.Capture(Settings, CredentialStore);

            return await AgentSetupTransaction.ExecuteAsync(
                attempt: async token =>
                {
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();
                    token.ThrowIfCancellationRequested();
                    return await RunSetupCandidateAsync(
                        candidate,
                        requiresDurableEnrollment,
                        stagingDirectory,
                        token);
                },
                commit: async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();
                    token.ThrowIfCancellationRequested();

                    stagedArtifacts = AgentManagedState.CaptureSetupArtifacts(stagingDirectory);

                    // Remove every staged copy, including the one-use token,
                    // before publishing any replacement state.
                    DeleteSetupDirectory(stagingDirectory);
                    token.ThrowIfCancellationRequested();

                    var committed = candidate.CloneForSetup();
                    committed.LocalApiAuthToken = string.Empty;
                    if (requiresDurableEnrollment)
                    {
                        committed.EnrollmentToken = string.Empty;
                        committed.GroupId = string.Empty;
                    }

                    persistenceTouched = true;
                    PersistCommittedSetup(committed, stagedArtifacts);
                    token.ThrowIfCancellationRequested();

                    StartAgent();
                    if (!AgentProcess.IsRunning)
                    {
                        throw new InvalidOperationException(
                            "The committed agent core did not start.");
                    }
                },
                rollback: async () =>
                {
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();

                    if (persistenceTouched)
                        snapshot.Restore(Settings, CredentialStore);

                    DeleteSetupDirectory(stagingDirectory);
                    if (priorWasRunning)
                    {
                        StartAgent();
                        if (!AgentProcess.IsRunning)
                            throw new InvalidOperationException("The previous agent core could not be restarted.");
                    }
                },
                cancellationToken);
        }
        catch (Exception ex) when (AgentSetupTransaction.IsRecoverable(ex))
        {
            return AgentConnectionAttemptResult.Failed(
                "The staged setup could not be prepared securely. The active setup was not replaced. Check Windows permissions and try again.");
        }
        finally
        {
            if (stagedArtifacts != null)
                AgentManagedState.ZeroArtifacts(stagedArtifacts);
            try
            {
                DeleteSetupDirectory(stagingDirectory);
            }
            catch (Exception ex) when (AgentSetupTransaction.IsRecoverable(ex))
            {
                AgentProcess.LogReader.AppendRaw(
                    "Could not remove the abandoned setup staging directory; no setup credentials were committed.");
            }
            _setupTransactionActive = false;
        }
    }

    private async Task<AgentConnectionAttemptResult> RunSetupCandidateAsync(
        AgentSettings candidate,
        bool requiresDurableEnrollment,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var terminalFailure = new TaskCompletionSource<AgentConnectionAttemptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleStatus(AgentStatus status)
        {
            var message = AgentSetupStatusClassifier.TerminalFailureMessage(status);
            if (message != null)
                terminalFailure.TrySetResult(AgentConnectionAttemptResult.Failed(message));
        }

        void HandleStartError(string _)
        {
            terminalFailure.TrySetResult(AgentConnectionAttemptResult.Failed(
                "The agent core could not be started. Reinstall LabTether Agent from a verified release and try again."));
        }

        ApiClient.OnStatusUpdated += HandleStatus;
        AgentProcess.OnError += HandleStartError;
        try
        {
            StartAgentWithSettings(candidate, stagingDirectory, completesEnrollment: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SetupConnectionTimeout);

            while (true)
            {
                if (terminalFailure.Task.IsCompleted)
                    return await terminalFailure.Task;

                var durableCredentialReady = !requiresDurableEnrollment ||
                    AgentManagedState.IsSetupStateReady(stagingDirectory);
                if (ApiClient.IsConnected && durableCredentialReady)
                    return AgentConnectionAttemptResult.Connected();

                if (!AgentProcess.IsRunning && !AgentProcess.IsStarting)
                {
                    return AgentConnectionAttemptResult.Failed(
                        "The agent core exited before setup established an authenticated Hub connection. Check the agent logs and try again.");
                }

                try
                {
                    var readinessDelay = Task.Delay(SetupReadinessPollInterval, timeout.Token);
                    var completed = await Task.WhenAny(terminalFailure.Task, readinessDelay);
                    if (completed == terminalFailure.Task)
                        return await terminalFailure.Task;
                    await readinessDelay;
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return AgentConnectionAttemptResult.Failed(
                        "The agent started, but did not establish an authenticated, durable Hub connection. Check the token and agent logs, then try again.");
                }
            }
        }
        finally
        {
            ApiClient.OnStatusUpdated -= HandleStatus;
            AgentProcess.OnError -= HandleStartError;
        }
    }

    private void PersistCommittedSetup(
        AgentSettings committed,
        IReadOnlyDictionary<string, byte[]> stagedArtifacts)
    {
        var settingsDirectory = AgentSettings.GetSettingsDirectory();
        AgentManagedState.CommitArtifacts(settingsDirectory, stagedArtifacts);
        SecureFile.DeleteIfExists(Path.Combine(settingsDirectory, "enrollment-token"));
        SecureFile.DeleteIfExists(Path.Combine(settingsDirectory, "enrollment-token.sha256"));

        committed.Save();
        CredentialStore.SaveFrom(committed);
        Settings.ApplyCommittedSetup(committed);
    }

    private static void DeleteSetupDirectory(string stagingDirectory)
    {
        var fullPath = Path.GetFullPath(stagingDirectory);
        var settingsDirectory = Path.GetFullPath(AgentSettings.GetSettingsDirectory());
        if (!string.Equals(Path.GetDirectoryName(fullPath), settingsDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith(".setup-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to remove an untrusted setup staging directory.");
        }

        if (!Directory.Exists(fullPath))
            return;

        var root = new DirectoryInfo(fullPath);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to remove a redirected setup staging directory.");

        foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to remove redirected setup state.");
        }
        Directory.Delete(fullPath, recursive: true);
    }

    private sealed class SetupPersistenceSnapshot : IDisposable
    {
        private readonly AgentSettings _settings;
        private readonly FileSnapshot[] _files;
        private readonly string? _apiToken;
        private readonly string? _enrollmentToken;
        private readonly string? _turnPassword;

        private SetupPersistenceSnapshot(
            AgentSettings settings,
            FileSnapshot[] files,
            string? apiToken,
            string? enrollmentToken,
            string? turnPassword)
        {
            _settings = settings;
            _files = files;
            _apiToken = apiToken;
            _enrollmentToken = enrollmentToken;
            _turnPassword = turnPassword;
        }

        public static SetupPersistenceSnapshot Capture(
            AgentSettings settings,
            CredentialStore credentialStore)
        {
            var directory = AgentSettings.GetSettingsDirectory();
            var filePaths = AgentManagedState.SnapshotArtifactNames
                .Select(fileName => Path.Combine(directory, fileName))
                .Concat(new[]
                {
                    AgentSettings.GetSettingsPath(),
                    Path.Combine(directory, "enrollment-token"),
                    Path.Combine(directory, "enrollment-token.sha256"),
                    Path.Combine(directory, "local-api-auth-token"),
                    Path.Combine(directory, "webrtc-turn-password"),
                });
            return new SetupPersistenceSnapshot(
                settings.CloneForSetup(),
                filePaths.Select(FileSnapshot.Capture).ToArray(),
                credentialStore.Retrieve(CredentialStore.ApiTokenResource),
                credentialStore.Retrieve(CredentialStore.EnrollmentTokenResource),
                credentialStore.Retrieve(CredentialStore.WebRtcTurnPassResource));
        }

        public void Restore(AgentSettings settings, CredentialStore credentialStore)
        {
            foreach (var file in _files)
                file.Restore();

            credentialStore.Store(CredentialStore.ApiTokenResource, _apiToken ?? string.Empty);
            credentialStore.Store(
                CredentialStore.EnrollmentTokenResource,
                _enrollmentToken ?? string.Empty);
            credentialStore.Store(
                CredentialStore.WebRtcTurnPassResource,
                _turnPassword ?? string.Empty);
            credentialStore.Remove(CredentialStore.LocalApiAuthResource);
            settings.ApplyCommittedSetup(_settings);
        }

        public void Dispose()
        {
            foreach (var file in _files)
                file.Dispose();
        }
    }

    private sealed class FileSnapshot : IDisposable
    {
        private readonly string _path;
        private readonly byte[]? _contents;

        private FileSnapshot(string path, byte[]? contents)
        {
            _path = path;
            _contents = contents;
        }

        public static FileSnapshot Capture(string path)
        {
            if (!File.Exists(path))
                return new FileSnapshot(path, null);

            var info = new FileInfo(path);
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Refusing to snapshot redirected setup state.");
            return new FileSnapshot(path, File.ReadAllBytes(path));
        }

        public void Restore()
        {
            if (_contents == null)
                SecureFile.DeleteIfExists(_path);
            else
                SecureFile.WriteAllBytes(_path, _contents);
        }

        public void Dispose()
        {
            if (_contents != null)
                CryptographicOperations.ZeroMemory(_contents);
        }
    }

}
