using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class AgentEnvironmentBuilderTests
{
    [Fact]
    public void BuildEnvironment_SetsHubUrlAndDerivedApiBase()
    {
        var settings = new AgentSettings { HubUrl = "https://hub.example.com" };
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "test-auth");

        Assert.Equal("wss://hub.example.com/ws/agent", env["LABTETHER_WS_URL"]);
        Assert.Equal("https://hub.example.com", env["LABTETHER_API_BASE_URL"]);
    }

    [Fact]
    public void BuildEnvironment_SetsLocalApiPortAndAuth()
    {
        var settings = new AgentSettings();
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "12345", "my-auth-token");

        Assert.Equal("12345", env["AGENT_PORT"]);
        Assert.Equal("my-auth-token", env["LABTETHER_LOCAL_API_AUTH_TOKEN"]);
    }

    [Fact]
    public void BuildEnvironment_OmitsEmptyAssetId()
    {
        var settings = new AgentSettings { AssetId = "" };
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.False(env.ContainsKey("AGENT_ASSET_ID"));
    }

    [Fact]
    public void BuildEnvironment_IncludesNonEmptyAssetId()
    {
        var settings = new AgentSettings { AssetId = "my-server" };
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.Equal("my-server", env["AGENT_ASSET_ID"]);
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
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.Equal("true", env["LABTETHER_AUTO_UPDATE"]);
        Assert.Equal("false", env["LABTETHER_ALLOW_REMOTE_OVERRIDES"]);
        Assert.Equal("true", env["LABTETHER_LOW_POWER_MODE"]);
        Assert.Equal("false", env["LABTETHER_WEBRTC_ENABLED"]);
        Assert.Equal("true", env["LABTETHER_TLS_SKIP_VERIFY"]);
    }

    [Fact]
    public void BuildEnvironment_DisablesLogStreamByDefault()
    {
        var settings = new AgentSettings();
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

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
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.Equal("auto", env["LABTETHER_DOCKER_ENABLED"]);
        Assert.Equal(@"\\.\pipe\docker_engine", env["LABTETHER_DOCKER_SOCKET"]);
        Assert.Equal("60", env["LABTETHER_DOCKER_DISCOVERY_INTERVAL"]);
    }

    [Fact]
    public void BuildEnvironment_SetsParentPid()
    {
        var settings = new AgentSettings();
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.True(env.ContainsKey("LABTETHER_PARENT_PID"));
        Assert.True(int.TryParse(env["LABTETHER_PARENT_PID"], out var pid));
        Assert.True(pid > 0);
    }

    [Fact]
    public void BuildEnvironment_SetsLogLevel()
    {
        var settings = new AgentSettings { LogLevel = "DEBUG" };
        var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "9090", "auth");

        Assert.Equal("debug", env["LABTETHER_LOG_LEVEL"]);
    }
}
