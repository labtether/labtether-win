using LabTetherAgent.Settings;
using System.Security.AccessControl;

namespace LabTetherAgent.Tests.Settings;

public class AgentEnvironmentBuilderTests
{
    private readonly string _secretDirectory = Path.Combine(
        Path.GetTempPath(),
        "LabTetherAgentTests",
        Guid.NewGuid().ToString("N"));

    private Dictionary<string, string> Build(AgentSettings settings, string port, string auth) =>
        AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(settings, port, auth, _secretDirectory);

    [Fact]
    public void BuildEnvironment_SetsHubUrlAndDerivedApiBase()
    {
        var settings = new AgentSettings { HubUrl = "https://hub.example.com" };
        var env = Build(settings, "9090", "test-auth");

        Assert.Equal("wss://hub.example.com/ws/agent", env["LABTETHER_WS_URL"]);
        Assert.Equal("https://hub.example.com", env["LABTETHER_API_BASE_URL"]);
        Assert.False(env.ContainsKey("LABTETHER_OUTBOUND_ALLOW_LOOPBACK"));
    }

    [Theory]
    [InlineData("wss://localhost:29443/ws/agent")]
    [InlineData("wss://127.0.0.1:29443/ws/agent")]
    [InlineData("wss://[::1]:29443/ws/agent")]
    public void BuildEnvironment_AllowsLoopbackOutboundForLoopbackHub(string hubUrl)
    {
        var settings = new AgentSettings { HubUrl = hubUrl };
        var env = Build(settings, "9090", "test-auth");

        Assert.Equal("true", env["LABTETHER_OUTBOUND_ALLOW_LOOPBACK"]);
    }

    [Fact]
    public void BuildEnvironment_SetsLocalApiPortAndAuth()
    {
        var settings = new AgentSettings();
        var env = Build(settings, "12345", "my-auth-token");

        Assert.Equal("12345", env["AGENT_PORT"]);
        Assert.False(env.ContainsKey("LABTETHER_LOCAL_API_AUTH_TOKEN"));
        var secretPath = env["LABTETHER_AGENT_LOCAL_AUTH_TOKEN_FILE"];
        Assert.Equal("my-auth-token", File.ReadAllText(secretPath).Trim());
        Assert.True(new FileInfo(secretPath).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void BuildEnvironment_PropagatesAuthenticationTurnAndCustomCAUsingSecretFiles()
    {
        var settings = new AgentSettings
        {
            ApiToken = "api-secret",
            WebRtcTurnPass = "turn-secret",
            TlsCaFile = @"C:\LabTether\ca.crt",
        };

        var env = Build(settings, "9090", "local-secret");

        Assert.Equal("api-secret", File.ReadAllText(env["LABTETHER_TOKEN_FILE"]).Trim());
        Assert.Equal("turn-secret", File.ReadAllText(env["LABTETHER_WEBRTC_TURN_PASS_FILE"]).Trim());
        Assert.Equal(@"C:\LabTether\ca.crt", env["LABTETHER_TLS_CA_FILE"]);
        Assert.False(env.ContainsKey("LABTETHER_API_TOKEN"));
        Assert.False(env.ContainsKey("LABTETHER_ENROLLMENT_TOKEN"));
        Assert.False(env.ContainsKey("LABTETHER_WEBRTC_TURN_PASS"));
    }

    [Fact]
    public void BuildEnvironment_EnrollmentLifecycleDoesNotReviveOrShadowStaleTokens()
    {
        var first = new AgentSettings { EnrollmentToken = "enroll-one" };
        var firstEnv = Build(first, "9090", "local-secret");
        var agentTokenPath = firstEnv["LABTETHER_TOKEN_FILE"];
        Assert.False(File.Exists(agentTokenPath));
        Assert.Equal("enroll-one", File.ReadAllText(firstEnv["LABTETHER_ENROLLMENT_TOKEN_FILE"]).Trim());

        // Simulate the Go child persisting the token returned by enrollment.
        SecureFile.WriteAllText(agentTokenPath, "issued-agent-token\n");
        Build(first, "9090", "local-secret");
        Assert.Equal("issued-agent-token", File.ReadAllText(agentTokenPath).Trim());

        // Once the one-use token is cleared, relaunch must retain only the
        // durable bearer and remove the enrollment material.
        var durableOnly = new AgentSettings();
        var durableEnv = Build(durableOnly, "9090", "local-secret");
        Assert.Equal(agentTokenPath, durableEnv["LABTETHER_TOKEN_FILE"]);
        Assert.Equal("issued-agent-token", File.ReadAllText(agentTokenPath).Trim());
        Assert.False(durableEnv.ContainsKey("LABTETHER_ENROLLMENT_TOKEN_FILE"));
        Assert.False(File.Exists(Path.Combine(_secretDirectory, "enrollment-token")));
        Assert.False(File.Exists(Path.Combine(_secretDirectory, "enrollment-token.sha256")));

        // A newly configured enrollment token must not be shadowed by that
        // prior issued token.
        var changed = new AgentSettings { EnrollmentToken = "enroll-two" };
        Build(changed, "9090", "local-secret");
        Assert.False(File.Exists(agentTokenPath));

        // Clearing authentication removes every stale credential file.
        Build(new AgentSettings(), "9090", "local-secret");
        Assert.False(File.Exists(agentTokenPath));
        Assert.False(File.Exists(Path.Combine(_secretDirectory, "enrollment-token")));
        Assert.False(File.Exists(Path.Combine(_secretDirectory, "enrollment-token.sha256")));
    }

    [Theory]
    [InlineData("", "", false, false)]
    [InlineData("", "", true, true)]
    [InlineData("valid-api-token", "", false, true)]
    [InlineData("", "valid-enrollment-token", false, true)]
    public void MinimumCredentialConfiguredHonorsDurableToken(
        string apiToken,
        string enrollmentToken,
        bool hasPersistedAgentToken,
        bool expected)
    {
        Assert.Equal(
            expected,
            AgentSettings.MinimumCredentialConfigured(
                apiToken,
                enrollmentToken,
                hasPersistedAgentToken));
    }

    [Fact]
    public void BuildEnvironment_RemovesClearedTurnPassword()
    {
        var configured = new AgentSettings { WebRtcTurnPass = "turn-secret" };
        var env = Build(configured, "9090", "local-secret");
        var turnPath = env["LABTETHER_WEBRTC_TURN_PASS_FILE"];
        Assert.True(File.Exists(turnPath));

        Build(new AgentSettings(), "9090", "local-secret");
        Assert.False(File.Exists(turnPath));
    }

    [Fact]
    public void BuildEnvironment_OmitsEmptyAssetId()
    {
        var settings = new AgentSettings { AssetId = "" };
        var env = Build(settings, "9090", "auth");

        Assert.False(env.ContainsKey("AGENT_ASSET_ID"));
    }

    [Fact]
    public void BuildEnvironment_IncludesNonEmptyAssetId()
    {
        var settings = new AgentSettings { AssetId = "my-server" };
        var env = Build(settings, "9090", "auth");

        Assert.Equal("my-server", env["AGENT_ASSET_ID"]);
    }

    [Fact]
    public void BuildEnvironment_ExportsGroupOnlyForFreshEnrollment()
    {
        var enrollment = new AgentSettings
        {
            GroupId = " qa ",
            EnrollmentToken = "fresh-enrollment-token",
        };
        var enrollmentEnv = Build(enrollment, "9090", "auth");
        Assert.Equal("qa", enrollmentEnv["AGENT_GROUP_ID"]);

        // Simulate the durable asset-bound bearer committed by the child.
        SecureFile.WriteAllText(
            Path.Combine(_secretDirectory, "agent-token"),
            "issued-agent-token\n");
        var durable = new AgentSettings { GroupId = "qa" };
        var durableEnv = Build(durable, "9090", "auth");
        Assert.False(durableEnv.ContainsKey("AGENT_GROUP_ID"));

        var apiToken = new AgentSettings
        {
            GroupId = "qa",
            ApiToken = "operator-api-token",
        };
        var apiTokenEnv = Build(apiToken, "9090", "auth");
        Assert.False(apiTokenEnv.ContainsKey("AGENT_GROUP_ID"));
    }

    [Fact]
    public void BuildEnvironment_MapsBooleansToStrings()
    {
        var settings = new AgentSettings
        {
            AutoUpdateEnabled = true,
            AllowRemoteOverrides = false,
            LowPowerMode = true,
            WebRtcEnabled = false,
            TlsSkipVerify = true,
        };
        var env = Build(settings, "9090", "auth");

        // A stale legacy preference must never let the bundled child replace
        // itself independently of the signed native app.
        Assert.Equal("false", env["LABTETHER_AUTO_UPDATE"]);
        Assert.Equal("false", env["LABTETHER_ALLOW_REMOTE_OVERRIDES"]);
        Assert.Equal("true", env["LABTETHER_LOW_POWER_MODE"]);
        Assert.Equal("false", env["LABTETHER_WEBRTC_ENABLED"]);
        Assert.Equal("true", env["LABTETHER_TLS_SKIP_VERIFY"]);
    }

    [Fact]
    public void BuildEnvironment_LegacyTlsConflictSecurelyPrefersCustomCa()
    {
        var settings = new AgentSettings
        {
            TlsSkipVerify = true,
            TlsCaFile = @"C:\LabTether\ca.crt",
        };

        var env = Build(settings, "9090", "auth");

        Assert.Equal(@"C:\LabTether\ca.crt", env["LABTETHER_TLS_CA_FILE"]);
        Assert.False(env.ContainsKey("LABTETHER_TLS_SKIP_VERIFY"));
    }

    [Fact]
    public void BuildEnvironment_DisablesLogStreamByDefault()
    {
        var settings = new AgentSettings();
        var env = Build(settings, "9090", "auth");

        Assert.Equal("false", env["LABTETHER_LOG_STREAM_ENABLED"]);
    }

    [Fact]
    public void BuildEnvironment_SetsDockerSettings()
    {
        var settings = new AgentSettings
        {
            DockerEnabled = "auto",
            DockerEndpoint = @"\\.\pipe\docker_engine",
            DockerDiscoveryInterval = "60",
        };
        var env = Build(settings, "9090", "auth");

        Assert.Equal("auto", env["LABTETHER_DOCKER_ENABLED"]);
        Assert.Equal(@"\\.\pipe\docker_engine", env["LABTETHER_DOCKER_SOCKET"]);
        Assert.Equal("60", env["LABTETHER_DOCKER_DISCOVERY_INTERVAL"]);
    }

    [Theory]
    [InlineData("30abc")]
    [InlineData("1e3")]
    [InlineData("9999")]
    public void BuildEnvironment_DefaultsMalformedDockerInterval(string interval)
    {
        var settings = new AgentSettings { DockerDiscoveryInterval = interval };
        var env = Build(settings, "9090", "auth");

        Assert.Equal("30", env["LABTETHER_DOCKER_DISCOVERY_INTERVAL"]);
    }

    [Fact]
    public void BuildEnvironment_SetsParentPid()
    {
        var settings = new AgentSettings();
        var env = Build(settings, "9090", "auth");

        Assert.True(env.ContainsKey("LABTETHER_PARENT_PID"));
        Assert.True(int.TryParse(env["LABTETHER_PARENT_PID"], out var pid));
        Assert.True(pid > 0);
    }

    [Fact]
    public void BuildEnvironment_SetsLogLevel()
    {
        var settings = new AgentSettings { LogLevel = "DEBUG" };
        var env = Build(settings, "9090", "auth");

        Assert.Equal("debug", env["LABTETHER_LOG_LEVEL"]);
    }
}
