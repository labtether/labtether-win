using LabTetherAgent.State;

namespace LabTetherAgent.Tests.State;

public class AgentStatusTests
{
    [Theory]
    [InlineData(false, "connecting", "Connecting")]
    [InlineData(false, "auth_failed", "The Hub refused the agent token")]
    [InlineData(false, "disconnected", "Disconnected")]
    [InlineData(false, "unexpected_internal_state", "Disconnected")]
    [InlineData(true, "connecting", "Connected")]
    public void ConnectionDisplayTextPreservesActionableKnownStates(
        bool connected,
        string state,
        string expected)
    {
        var status = new AgentStatus
        {
            IsConnected = connected,
            HubConnectionState = state,
        };

        Assert.Equal(expected, status.ConnectionDisplayText);
    }

    // Every last_error_reason the agent sends (ws_connect_reason.go) gets its
    // own words, with the meaning the Mac app shows. Refused, unreachable and
    // timed out all read "Can't reach the Hub" before, and a refused agent
    // token read "The Hub refused this device".
    [Theory]
    [InlineData(false, "connecting", "name_lookup_failed", "Can't find the Hub's host name")]
    [InlineData(false, "connecting", "connection_refused", "The Hub refused the connection")]
    [InlineData(false, "connecting", "host_unreachable", "Can't reach the Hub")]
    [InlineData(false, "connecting", "timed_out", "The Hub did not answer in time")]
    [InlineData(false, "connecting", "certificate_untrusted", "The Hub's certificate is not trusted")]
    [InlineData(false, "connecting", "token_rejected", "The Hub refused the agent token")]
    [InlineData(false, "connecting", "", "Connecting")]
    [InlineData(false, "connecting", "a_future_reason", "Connecting")]
    [InlineData(true, "connected", "timed_out", "Connected")]
    public void ConnectionDisplayTextShowsWhyTheHubConnectFailed(
        bool connected,
        string state,
        string reason,
        string expected)
    {
        var status = new AgentStatus
        {
            IsConnected = connected,
            HubConnectionState = state,
            LastErrorReason = reason,
        };

        Assert.Equal(expected, status.ConnectionDisplayText);
    }

    // last_error codes the agent sends with auth_failed (ws_transport.go,
    // run.go). The code's words, with what to do, win over the reason.
    [Theory]
    [InlineData("auth_failed", "token_rejected",
        "The Hub refused the agent token. Choose Re-enroll in the tray menu.")]
    [InlineData("enrollment_token_rejected", "",
        "The Hub refused this enrollment token. Make a new one in Devices, Add device.")]
    [InlineData("a_future_code", "", "The Hub refused the agent token")]
    public void RefusedTokenSaysWhichTokenAndWhatToDo(string lastError, string reason, string expected)
    {
        var status = new AgentStatus
        {
            HubConnectionState = "auth_failed",
            LastError = lastError,
            LastErrorReason = reason,
        };

        Assert.Equal(expected, status.ConnectionDisplayText);
    }

    [Fact]
    public void FiringAlerts_FiltersCorrectly()
    {
        var status = new AgentStatus
        {
            Alerts =
            [
                new AlertSnapshot("disk_high", "firing", "warning", "Disk at 92%"),
                new AlertSnapshot("cpu_high", "resolved", "warning", null),
                new AlertSnapshot("mem_critical", "firing", "critical", "OOM risk"),
            ]
        };

        Assert.Equal(2, status.FiringAlerts.Count);
        Assert.True(status.HasCriticalFiring);
    }

    [Fact]
    public void FiringAlerts_NoCritical()
    {
        var status = new AgentStatus
        {
            Alerts =
            [
                new AlertSnapshot("disk_high", "firing", "warning", null),
            ]
        };

        Assert.Single(status.FiringAlerts);
        Assert.False(status.HasCriticalFiring);
    }

    [Fact]
    public void MemoryDisplayText_WithTotalBytes()
    {
        var status = new AgentStatus
        {
            MemoryUsedBytes = (long)(4.2 * 1024 * 1024 * 1024),
            MemoryTotalBytes = 16L * 1024 * 1024 * 1024,
        };

        Assert.Equal("4.2 GB", status.MemoryDisplayText);
    }

    [Fact]
    public void MemoryDisplayText_WithoutTotalBytes_FallsBackToPercent()
    {
        var status = new AgentStatus
        {
            MemoryPercent = 65,
            MemoryTotalBytes = 0,
        };

        Assert.Equal("65%", status.MemoryDisplayText);
    }
}
