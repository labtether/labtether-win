using System.Net;
using System.Security.Authentication;
using LabTetherAgent.App;
using LabTetherAgent.Api;
using LabTetherAgent.Presentation;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using LabTetherAgent.Tests.Services;

namespace LabTetherAgent.Tests.Presentation;

// Setup may trust a home Hub's own CA only after the person says so. Until
// then nothing is saved and no token leaves the PC, and a token issued by one
// Hub must never reach a Hub that was trusted afterwards.
public class OnboardingTrustTests : IDisposable
{
    private const string HubA = "https://hub-a.example.test:8443";
    private const string HubB = "https://hub-b.example.test:8443";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
    private readonly List<string?> _verifiedWith = [];
    private readonly List<AgentSettings> _started = [];
    private readonly HubTrustOffer _offer;
    private Func<string, CancellationToken, Task<HubTrustDecision>> _probe;
    private bool _hubRejectsTheTrustedCa;

    public OnboardingTrustTests()
    {
        using var ca = TestCertificates.Ca("Hub B CA", DateTimeOffset.UtcNow);
        _offer = new HubTrustOffer(ca.ExportCertificatePem(), HubCertificateTrust.Fingerprint(ca));
        _probe = (_, _) => Task.FromResult(new HubTrustDecision(_offer, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task HubWithItsOwnCertificateAsksBeforeAnythingIsTrustedOrSent()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "new")));
        var viewModel = Create(settings, HubB, "hub-b-enrollment-token");

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrustPrompt);
        Assert.Equal("This Hub uses its own certificate", viewModel.TrustTitle);
        Assert.Equal(_offer.GroupedFingerprint, viewModel.TrustFingerprint.Replace("\n", " "));
        Assert.Equal(2, viewModel.TrustFingerprint.Split('\n').Length);
        Assert.False(viewModel.CanConnect);
        Assert.Empty(_started);

        // Cancel: nothing trusted, nothing saved, no token sent.
        viewModel.CancelTrustCommand.Execute(null);
        Assert.False(viewModel.HasTrustPrompt);
        Assert.Empty(_started);
        Assert.Empty(settings.TlsCaFile);
        Assert.False(File.Exists(settings.ManagedCaFilePath));

        // Trust this Hub: the Hub is verified against exactly that CA, and only
        // then does the agent start with the token.
        await viewModel.ConnectCommand.ExecuteAsync(null);
        await viewModel.TrustHubCommand.ExecuteAsync(null);
        Assert.Equal(_offer.CaPem, _verifiedWith[^1]);
        var candidate = Assert.Single(_started);
        Assert.Equal(_offer.CaPem, candidate.TrustedHubCaPem);
        Assert.Equal("hub-b-enrollment-token", candidate.EnrollmentToken);
        Assert.True(viewModel.IsConnected);
    }

    // Spec rule: a newly trusted CA never proves "same Hub". This device holds
    // a token for Hub A; the address is changed to Hub B and B is trusted.
    [Fact]
    public async Task TokenForOneHubIsNeverSentToANewlyTrustedHub()
    {
        var location = AppStateLocation.ForDirectory(Path.Combine(_root, "enrolled"));
        var settings = AgentSettings.Load(location);
        settings.HubUrl = HubA;
        settings.ApiToken = "hub-a-agent-token";
        settings.TlsCaFile = settings.ManagedCaFilePath;
        settings.EnrolledHubCaFingerprint = new string('a', 64);
        location.EnsureDirectory();
        SecureFile.WriteAllText(settings.ManagedCaFilePath, "hub-a-ca");
        var viewModel = Create(settings, HubB, "hub-b-enrollment-token");

        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasTrustPrompt);
        await viewModel.TrustHubCommand.ExecuteAsync(null);

        // What starts against Hub B carries only the token typed for B.
        var candidate = Assert.Single(_started);
        Assert.Equal(HubB, candidate.HubUrl);
        Assert.Empty(candidate.ApiToken);
        Assert.Equal("hub-b-enrollment-token", candidate.EnrollmentToken);
        Assert.DoesNotContain("hub-a-agent-token", EnvironmentValues(candidate, Path.Combine(_root, "setup")));
        // Hub A's token, address, CA and record stay as they were until setup commits.
        Assert.Equal(HubA, settings.HubUrl);
        Assert.Equal("hub-a-agent-token", settings.ApiToken);
        Assert.Equal(new string('a', 64), settings.EnrolledHubCaFingerprint);
        Assert.Equal("hub-a-ca", File.ReadAllText(settings.ManagedCaFilePath));
    }

    // A failed setup must leave the enrolled Hub's CA in place: the token for
    // Hub A is still saved, so its CA may not be replaced by B's.
    [Fact]
    public async Task FailedSetupKeepsTheEnrolledHubsCa()
    {
        var location = AppStateLocation.ForDirectory(Path.Combine(_root, "owner"));
        var settings = AgentSettings.Load(location);
        settings.HubUrl = HubA;
        settings.TlsCaFile = settings.ManagedCaFilePath;
        settings.Save();
        SecureFile.WriteAllText(settings.ManagedCaFilePath, "hub-a-ca");
        using var owner = new AppState(
            settings, new CredentialStore(location), new AgentProcess(), new LocalApiClient(), () => null);
        var candidate = settings.CloneForSetup();
        candidate.HubUrl = HubB;
        candidate.EnrollmentToken = "hub-b-enrollment-token";
        candidate.TrustedHubCaPem = _offer.CaPem;

        var result = await owner.ConnectAgentForSetupAsync(candidate, true, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("hub-a-ca", File.ReadAllText(settings.ManagedCaFilePath));
        Assert.Equal(HubA, AgentSettings.Load(location).HubUrl);
        Assert.Empty(Directory.GetDirectories(location.DirectoryPath, ".setup-*"));
    }

    [Fact]
    public async Task SameHubWithAnotherCaSaysTheCertificateChanged()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "changed")));
        settings.HubUrl = HubB;
        settings.ApiToken = "hub-b-agent-token";
        settings.EnrolledHubCaFingerprint = new string('b', 64);
        var viewModel = Create(settings, HubB, "new-enrollment-token");

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrustPrompt);
        Assert.Equal("This Hub's certificate changed", viewModel.TrustTitle);
        Assert.Empty(_started);
    }

    // Right after Trust this Hub another server answers with its own CA. That
    // is a change and must be said as one, not offered as a first-time trust;
    // and a CA that was accepted but does not verify the Hub is not asked twice.
    [Fact]
    public async Task AnotherCaRightAfterTrustIsCalledAChangeAndTheSameCaIsNotAskedTwice()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "retry")));
        var viewModel = Create(settings, HubB, "hub-b-enrollment-token");
        using var otherCa = TestCertificates.Ca("Another CA", DateTimeOffset.UtcNow);
        var other = new HubTrustOffer(otherCa.ExportCertificatePem(), HubCertificateTrust.Fingerprint(otherCa));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("This Hub uses its own certificate", viewModel.TrustTitle);
        _hubRejectsTheTrustedCa = true;
        _probe = (_, _) => Task.FromResult(new HubTrustDecision(other, null));
        await viewModel.TrustHubCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrustPrompt);
        Assert.Equal("This Hub's certificate changed", viewModel.TrustTitle);
        Assert.Empty(_started);

        await viewModel.TrustHubCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasTrustPrompt);
        Assert.Equal(HubCertificateTrust.TrustedCaDidNotVerify, viewModel.ConnectionError);
        Assert.Empty(_started);
    }

    // The fields stay editable while Connect runs. An offer read for one
    // address must not appear, or be trusted, for an address typed meanwhile.
    [Fact]
    public async Task OfferForAnAddressEditedMeanwhileIsDropped()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "edited")));
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<HubTrustDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _probe = async (_, _) =>
        {
            probeStarted.TrySetResult();
            return await answer.Task;
        };
        var viewModel = Create(settings, HubB, "hub-b-enrollment-token");

        var connect = viewModel.ConnectCommand.ExecuteAsync(null);
        await probeStarted.Task;
        viewModel.HubUrl = HubA;
        answer.SetResult(new HubTrustDecision(_offer, null));
        await connect;

        Assert.False(viewModel.HasTrustPrompt);
        Assert.Null(viewModel.ConnectionError);
        Assert.Empty(_started);
    }

    // The setup agent must verify the new Hub with the CA staged for it, not
    // with the copy saved for the Hub this device is enrolled with.
    [Fact]
    public void ManagedCaFollowsTheSetupDirectory()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "env")));
        settings.HubUrl = HubB;
        settings.EnrollmentToken = "hub-b-enrollment-token";
        settings.TlsCaFile = settings.ManagedCaFilePath;
        var setup = Path.Combine(_root, "env", ".setup-test");

        var environment = AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(settings, "8091", "auth", setup);

        Assert.Equal(Path.Combine(setup, "ca.crt"), environment["LABTETHER_TLS_CA_FILE"]);
        Assert.Equal(settings.ManagedCaFilePath,
            AgentEnvironmentBuilder.BuildEnvironment(settings, "8091", "auth")["LABTETHER_TLS_CA_FILE"]);
    }

    // Setup's own test trusts only a CA file chosen under Advanced. When it
    // passes, the setup agent must trust only that file too, or the token
    // could go to a server that Windows' certificate list accepts.
    [Fact]
    public async Task ChosenCaFileThatVerifiedTheHubIsTheSetupAgentsOnlyRoot()
    {
        var settings = AgentSettings.Load(AppStateLocation.ForDirectory(Path.Combine(_root, "chosen")));
        Directory.CreateDirectory(_root);
        var caFile = Path.Combine(_root, "chosen-ca.pem");
        using (var ca = TestCertificates.Ca("Chosen CA", DateTimeOffset.UtcNow))
            File.WriteAllText(caFile, ca.ExportCertificatePem());
        var tester = new ConnectionTester((_, chosen, _) => new StubHandler(() => chosen != null
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"service\":\"labtether-hub\"}") }
            : throw new HttpRequestException("TLS", new AuthenticationException("untrusted root"))));
        var viewModel = new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: Path.Combine(_root, Guid.NewGuid().ToString("N"), ".credentials")),
            tester,
            (candidate, _, _) =>
            {
                _started.Add(candidate);
                return Task.FromResult(AgentConnectionAttemptResult.Connected());
            },
            (url, cancellation) => _probe(url, cancellation))
        {
            HubUrl = HubB,
            Token = "hub-b-enrollment-token",
            TlsCaFile = caFile,
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        var candidate = Assert.Single(_started);
        Assert.True(candidate.UsesVerifiedCaFile);
        var environment = AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(
            candidate, "8091", "auth", Path.Combine(_root, "chosen", ".setup-test"));
        Assert.Equal(caFile, environment["LABTETHER_TLS_CA_FILE"]);
        Assert.Equal("true", environment["LABTETHER_TLS_CA_ONLY"]);
    }

    private OnboardingViewModel Create(AgentSettings settings, string hubUrl, string token)
    {
        // Windows rejects the Hub's certificate until a CA accepted in the panel is supplied.
        var tester = new ConnectionTester((_, _, caPem) => new StubHandler(() =>
        {
            _verifiedWith.Add(caPem);
            if (caPem == null || _hubRejectsTheTrustedCa)
                throw new HttpRequestException("TLS", new AuthenticationException("untrusted root"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"service\":\"labtether-hub\"}") };
        }));
        return new OnboardingViewModel(
            settings,
            new CredentialStore(vaultAvailable: false, fallbackPath: Path.Combine(_root, Guid.NewGuid().ToString("N"), ".credentials")),
            tester,
            (candidate, _, _) =>
            {
                _started.Add(candidate);
                return Task.FromResult(AgentConnectionAttemptResult.Connected());
            },
            (url, cancellation) => _probe(url, cancellation))
        {
            HubUrl = hubUrl,
            Token = token,
        };
    }

    private static string EnvironmentValues(AgentSettings candidate, string setupDirectory)
    {
        var environment = AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(candidate, "8091", "auth", setupDirectory);
        var files = Directory.GetFiles(setupDirectory).Select(File.ReadAllText);
        return string.Join('\n', environment.Values.Concat(files));
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
