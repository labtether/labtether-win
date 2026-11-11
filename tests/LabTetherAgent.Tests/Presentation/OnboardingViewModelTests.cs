using LabTetherAgent.Presentation;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using System.Net;

namespace LabTetherAgent.Tests.Presentation;

public class OnboardingViewModelTests
{
    // After the app's own copy of the Hub CA is lost, the start error says to
    // re-enroll. Setup must not prefill that missing path: Connect would only
    // say the file is unavailable, and the person never chose it.
    [Fact]
    public void ReenrollDoesNotOfferAManagedCaThatIsGone()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AgentSettings(AppStateLocation.ForDirectory(root)) { HubUrl = "https://nas01.example.com:8443" };
            settings.TlsCaFile = settings.ManagedCaFilePath;
            var credentials = new CredentialStore(vaultAvailable: false, fallbackPath: Path.Combine(root, ".credentials"));

            Assert.Empty(new OnboardingViewModel(settings, credentials, new ConnectionTester()).TlsCaFile);

            Directory.CreateDirectory(root);
            File.WriteAllText(settings.ManagedCaFilePath, "saved CA");
            Assert.Equal(settings.ManagedCaFilePath,
                new OnboardingViewModel(settings, credentials, new ConnectionTester()).TlsCaFile);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TokenTypeRadioButtonsRoundTripBetweenEnrollmentAndApiModes()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var viewModel = new OnboardingViewModel(
            new AgentSettings(),
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            new ConnectionTester());

        Assert.True(viewModel.UseEnrollmentToken);
        Assert.False(viewModel.UseApiToken);
        Assert.True(viewModel.CanSetGroupId);
        Assert.Equal(0, viewModel.TokenTypeIndex);

        // The window's radio group binds the index: 1 is an API token, and a
        // transient -1 while the group rebuilds must not change the choice.
        viewModel.TokenTypeIndex = 1;
        Assert.True(viewModel.UseApiToken);
        viewModel.TokenTypeIndex = -1;
        Assert.True(viewModel.UseApiToken);
        viewModel.TokenTypeIndex = 0;
        Assert.True(viewModel.UseEnrollmentToken);

        viewModel.GroupId = "enrollment-placement";

        viewModel.UseApiToken = true;

        Assert.False(viewModel.UseEnrollmentToken);
        Assert.True(viewModel.UseApiToken);
        Assert.False(viewModel.CanSetGroupId);
        Assert.Equal(string.Empty, viewModel.GroupId);

        viewModel.UseEnrollmentToken = true;

        Assert.True(viewModel.UseEnrollmentToken);
        Assert.False(viewModel.UseApiToken);
        Assert.True(viewModel.CanSetGroupId);
    }

    [Fact]
    public void ReEnrollmentPrefillsNonSecretConnectionStateOnly()
    {
        var settings = new AgentSettings
        {
            HubUrl = "wss://hub.example.test/ws/agent",
            AssetId = "windows-node",
            GroupId = "qa",
            ApiToken = "existing-secret-must-not-be-shown",
            TlsSkipVerify = false,
            TlsCaFile = @"C:\LabTether\ca.pem",
        };
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");

        var viewModel = new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            new ConnectionTester());

        Assert.Equal(settings.HubUrl, viewModel.HubUrl);
        Assert.Equal("windows-node", viewModel.AssetId);
        Assert.Equal("qa", viewModel.GroupId);
        Assert.Equal(@"C:\LabTether\ca.pem", viewModel.TlsCaFile);
        Assert.Equal(string.Empty, viewModel.Token);
    }

    [Fact]
    public async Task ReachableHubWithRejectedEnrollmentKeepsSetupOpenAndShowsCredentialError()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var settings = new AgentSettings
        {
            PersistedAgentTokenPathOverride = credentialPath + ".agent-token",
        };
        AgentSettings? stagedCandidate = null;
        bool? requiredDurableEnrollment = null;
        var viewModel = new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            VerifiedHubConnectionTester(),
            (candidate, requiresDurable, _) =>
            {
                stagedCandidate = candidate;
                requiredDurableEnrollment = requiresDurable;
                return Task.FromResult(AgentConnectionAttemptResult.Failed(
                    "The Hub refused this enrollment token. Make a new one in Devices, Add device."));
            })
        {
            HubUrl = "https://hub.example.test:8443",
            Token = "syntactically-valid-but-rejected-token",
        };
        var completed = false;
        viewModel.OnCompleted += () => completed = true;

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.NotNull(stagedCandidate);
        Assert.Equal("syntactically-valid-but-rejected-token", stagedCandidate!.EnrollmentToken);
        Assert.Equal("wss://localhost:8443/ws/agent", settings.HubUrl);
        Assert.Empty(settings.EnrollmentToken);
        Assert.False(settings.IsEnrolled);
        Assert.True(requiredDurableEnrollment is true);
        Assert.False(completed);
        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.IsConnecting);
        Assert.True(viewModel.CanConnect);
        Assert.Contains("refused this enrollment token", viewModel.ConnectionError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditingTokenAfterRejectedEnrollmentClearsStaleErrorAndRetryShowsOnlyNewFailure()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var attempt = 0;
        var viewModel = new OnboardingViewModel(
            new AgentSettings(),
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            RepeatableVerifiedHubConnectionTester(),
            (_, _, _) => Task.FromResult(AgentConnectionAttemptResult.Failed(
                ++attempt == 1
                    ? "The first enrollment token was rejected."
                    : "The replacement enrollment token was rejected.")))
        {
            HubUrl = "https://hub.example.test:8443",
            Token = "first-syntactically-valid-token",
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("The first enrollment token was rejected.", viewModel.ConnectionError);

        viewModel.Token = "replacement-syntactically-valid-token";

        Assert.Null(viewModel.ConnectionError);
        Assert.False(viewModel.IsConnected);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(2, attempt);
        Assert.Equal("The replacement enrollment token was rejected.", viewModel.ConnectionError);
    }

    // The one-screen setup has no step gate, so Connect itself must refuse an
    // incomplete form instead of probing an invalid Hub or sending no token.
    [Fact]
    public async Task ConnectDoesNothingUntilHubUrlAndTokenAreValid()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var agentStarted = false;
        var viewModel = new OnboardingViewModel(
            new AgentSettings(),
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            VerifiedHubConnectionTester(),
            (_, _, _) =>
            {
                agentStarted = true;
                return Task.FromResult(AgentConnectionAttemptResult.Connected());
            });

        Assert.False(viewModel.CanConnect);
        Assert.Null(viewModel.HubUrlError);

        viewModel.HubUrl = "hub.example.test";
        viewModel.Token = "valid-enrollment-token";
        Assert.False(viewModel.CanConnect);
        Assert.NotNull(viewModel.HubUrlError);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.False(agentStarted);

        viewModel.HubUrl = "https://hub.example.test:8443";
        viewModel.Token = "   ";
        Assert.False(viewModel.CanConnect);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.False(agentStarted);

        viewModel.Token = "valid-enrollment-token";
        Assert.True(viewModel.CanConnect);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.True(agentStarted);
    }

    [Fact]
    public async Task EditingDeviceIdClearsErrorFromPreviousSubmission()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var viewModel = new OnboardingViewModel(
            new AgentSettings(),
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            VerifiedHubConnectionTester(),
            (_, _, _) => Task.FromResult(AgentConnectionAttemptResult.Failed(
                "The submitted identity could not be enrolled.")))
        {
            HubUrl = "https://hub.example.test:8443",
            AssetId = "first-windows-node",
            Token = "syntactically-valid-enrollment-token",
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ConnectionError);

        viewModel.AssetId = "corrected-windows-node";

        Assert.Null(viewModel.ConnectionError);
        Assert.False(viewModel.IsConnected);
    }

    [Fact]
    public async Task SetupCompletesOnlyAfterAgentReportsAuthenticatedConnection()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var settings = new AgentSettings
        {
            PersistedAgentTokenPathOverride = credentialPath + ".agent-token",
        };
        var viewModel = new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            VerifiedHubConnectionTester(),
            (_, _, _) => Task.FromResult(AgentConnectionAttemptResult.Connected()))
        {
            HubUrl = "https://hub.example.test:8443",
            Token = "valid-enrollment-token",
        };
        var completed = false;
        viewModel.OnCompleted += () => completed = true;

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(completed);
        Assert.True(viewModel.IsConnected);
        Assert.Null(viewModel.ConnectionError);
        Assert.False(viewModel.IsConnecting);
    }

    [Fact]
    public async Task ClosingSetupDuringHubCheckDoesNotPersistOrStartAgent()
    {
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionTester = new ConnectionTester((_, _) => new DelegateHttpHandler(async _ =>
        {
            probeStarted.TrySetResult();
            return await releaseProbe.Task;
        }));
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var agentStarted = false;
        var viewModel = new OnboardingViewModel(
            new AgentSettings(),
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            connectionTester,
            (_, _, _) =>
            {
                agentStarted = true;
                return Task.FromResult(AgentConnectionAttemptResult.Connected());
            })
        {
            HubUrl = "https://hub.example.test:8443",
            Token = "unused-enrollment-token",
        };

        var finish = viewModel.ConnectCommand.ExecuteAsync(null);
        await probeStarted.Task;
        viewModel.CancelConnectionAttempt();
        releaseProbe.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"service\":\"labtether-hub\"}"),
        });
        await finish;

        Assert.False(agentStarted);
        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.IsConnecting);
    }

    [Fact]
    public async Task FirstInstallFailureDoesNotCreateFalseEnrollmentState()
    {
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var settings = new AgentSettings
        {
            PersistedAgentTokenPathOverride = credentialPath + ".agent-token",
        };
        var viewModel = new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath),
            VerifiedHubConnectionTester(),
            (_, _, _) => Task.FromResult(AgentConnectionAttemptResult.Failed(
                "The enrollment token was rejected.")))
        {
            HubUrl = "https://hub.example.test:8443",
            AssetId = "new-windows-node",
            Token = "rejected-first-install-token",
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(settings.IsEnrolled);
        Assert.Equal("wss://localhost:8443/ws/agent", settings.HubUrl);
        Assert.Empty(settings.AssetId);
        Assert.Empty(settings.EnrollmentToken);
        Assert.False(viewModel.IsConnected);
    }

    private static ConnectionTester VerifiedHubConnectionTester() =>
        new((_, _) => new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"service\":\"labtether-hub\"}"),
        }));

    private static ConnectionTester RepeatableVerifiedHubConnectionTester() =>
        new(_ => new DelegateHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"service\":\"labtether-hub\"}"),
        })));

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class DelegateHttpHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(cancellationToken);
    }
}
