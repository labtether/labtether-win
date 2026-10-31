using LabTetherAgent.State;

namespace LabTetherAgent.Tests.State;

public class AgentStatusTests
{
    [Theory]
    [InlineData(false, "connecting", "Connecting")]
    [InlineData(false, "auth_failed", "Authentication Failed")]
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

    // The tray and pop-out must say why the Hub connect failed, keep the state
    // text for older agents and unknown codes, and drop the reason once connected.
    [Theory]
    [InlineData(false, "connecting", "name_lookup_failed", "Can't find the Hub's address")]
    [InlineData(false, "connecting", "connection_refused", "Can't reach the Hub")]
    [InlineData(false, "connecting", "host_unreachable", "Can't reach the Hub")]
    [InlineData(false, "connecting", "timed_out", "Can't reach the Hub")]
    [InlineData(false, "connecting", "certificate_untrusted", "The Hub's certificate isn't trusted")]
    [InlineData(false, "auth_failed", "token_rejected", "The Hub refused this device. Enroll it again.")]
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
