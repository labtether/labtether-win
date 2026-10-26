using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

public class AppStateChildOwnershipTests
{
    [Fact]
    public async Task OldExitNotificationCannotDisconnectReplacement()
    {
        await using var fixture = new ChildFixture(firstChildExits: true, delayStop: false, delayExit: true);
        fixture.Start();
        await fixture.ExitQueued.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Start();
        await fixture.Api.PollStatusForTestingAsync();
        Assert.True(fixture.Api.IsConnected);
        var replacementToken = fixture.Settings.LocalApiAuthToken;

        fixture.DeliverExitNotifications();

        Assert.True(fixture.Owner.IsAgentRunning);
        Assert.True(fixture.Api.IsConnected);
        Assert.Equal(replacementToken, fixture.Settings.LocalApiAuthToken);
        await fixture.ExpectReplacementOutput();
    }

    [Fact]
    public async Task DelayedStopCannotCancelReplacementLogsOrDeleteItsAuth()
    {
        await using var fixture = new ChildFixture(firstChildExits: false, delayStop: true, delayExit: false);
        fixture.Start();
        var retiredStop = fixture.Owner.StopAgentAsync();
        await fixture.StopPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Start();
        await fixture.Api.PollStatusForTestingAsync();
        var replacementToken = fixture.Settings.LocalApiAuthToken;
        var replacementFile = File.ReadAllText(fixture.AuthPath);
        Assert.False(string.IsNullOrWhiteSpace(replacementToken));

        fixture.ReleaseStop.TrySetResult();
        await retiredStop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(fixture.Owner.IsAgentRunning);
        Assert.True(fixture.Api.IsConnected);
        Assert.Equal(replacementToken, fixture.Settings.LocalApiAuthToken);
        Assert.Equal(replacementFile, File.ReadAllText(fixture.AuthPath));
        await fixture.ExpectReplacementOutput();
    }

    private sealed class ChildFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"labtether-child-owner-{Guid.NewGuid():N}");
        private readonly string _gatePath;
        private readonly ConcurrentQueue<Action> _exitNotifications = new();
        private readonly List<System.Diagnostics.Process> _children = [];
        private readonly List<System.Diagnostics.Process> _ownedHandles = [];
        private readonly TaskCompletionSource _replacementOutput = NewSignal();
        private int _waitCount;
        public readonly TaskCompletionSource ExitQueued = NewSignal();
        public readonly TaskCompletionSource StopPaused = NewSignal();
        public readonly TaskCompletionSource ReleaseStop = NewSignal();
        public readonly AgentSettings Settings = new() { HubUrl = "https://child-owner.invalid" };
        public readonly LocalApiClient Api;
        public readonly AppState Owner;
        public string AuthPath => Path.Combine(_root, "local-api-auth-token");

        public ChildFixture(bool firstChildExits, bool delayStop, bool delayExit)
        {
            Directory.CreateDirectory(_root);
            _gatePath = Path.Combine(_root, "release-output");
            var binary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.True(File.Exists(binary));
            var process = new AgentProcess(info =>
            {
                info.ArgumentList.Add("-NoProfile");
                info.ArgumentList.Add("-NonInteractive");
                info.ArgumentList.Add("-Command");
                var command = _children.Count == 0
                    ? (firstChildExits ? "exit 7" : "Start-Sleep -Seconds 30")
                    : "while (!(Test-Path -LiteralPath $env:LT_OWNER_GATE)) { Start-Sleep -Milliseconds 20 }; Write-Output 'replacement-alive'; Start-Sleep -Seconds 30";
                info.ArgumentList.Add(command);
                info.Environment["LT_OWNER_GATE"] = _gatePath;
                var child = new System.Diagnostics.Process { StartInfo = info };
                _children.Add(child);
                return child;
            }, action =>
            {
                if (!delayExit) { action(); return; }
                _exitNotifications.Enqueue(action);
                ExitQueued.TrySetResult();
            }, async (child, timeout) =>
            {
                // These shell children do not handle the agent's console
                // signal. Shorten only their grace wait; preserve the real
                // wait contract so StopChildAsync can force-kill on timeout.
                var wait = timeout == TimeSpan.FromSeconds(10)
                    ? TimeSpan.FromMilliseconds(100) : timeout;
                using var cancellation = new CancellationTokenSource(wait);
                try { await child.WaitForExitAsync(cancellation.Token); }
                catch (OperationCanceledException) { return false; }
                if (delayStop && Interlocked.Increment(ref _waitCount) == 1)
                {
                    StopPaused.TrySetResult();
                    await ReleaseStop.Task;
                }
                return true;
            });
            process.LogReader.OnLogLine += line =>
            {
                if (line.Message == "replacement-alive") _replacementOutput.TrySetResult();
            };
            Api = new LocalApiClient(new HttpClient(new ConnectedHandler()));
            Owner = new AppState(Settings, new CredentialStore(false, Path.Combine(_root, "credentials")),
                process, Api, () => binary,
                // The fixture owns only its two children. Never enumerate or
                // kill other PowerShell processes on the shared QA host.
                killOrphanedAgents: _ => { });
        }

        public void Start()
        {
            Owner.StartAgentWithSettings(Settings, _root, completesEnrollment: false);
            var child = _children.Last();
            // Retain an independent handle to each exact live child so even
            // a failing owner assertion cannot leak a fixture process.
            try
            {
                if (!child.HasExited)
                {
                    var owned = System.Diagnostics.Process.GetProcessById(child.Id);
                    _ = owned.SafeHandle;
                    _ownedHandles.Add(owned);
                }
            }
            catch (InvalidOperationException) { } // The intentional exit-7 child.
            catch (ArgumentException) { }
        }

        public void DeliverExitNotifications()
        {
            while (_exitNotifications.TryDequeue(out var action)) action();
        }

        public async Task ExpectReplacementOutput()
        {
            File.WriteAllText(_gatePath, "release");
            await _replacementOutput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseStop.TrySetResult();
            try { await Owner.StopAgentAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            finally
            {
                var cleanupErrors = new List<Exception>();
                foreach (var child in _ownedHandles)
                {
                    try
                    {
                        if (!child.HasExited) child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex) { cleanupErrors.Add(ex); }
                    finally { child.Dispose(); }
                }
                try { DeliverExitNotifications(); }
                finally { Owner.Dispose(); }
                Directory.Delete(_root, recursive: true);
                if (cleanupErrors.Count != 0) throw new AggregateException(cleanupErrors);
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ConnectedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"connected\":true,\"connection_state\":\"connected\",\"metrics\":{}}")
            });
    }
}
