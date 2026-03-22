using LabTetherAgent.State;

namespace LabTetherAgent.Tests.State;

public class AgentStatusTests
{
    [Fact]
    public void ExtractWindowsStatus_HyperVEnabled()
    {
        var status = new AgentStatus
        {
            Metadata = new Dictionary<string, string>
            {
                ["hyperv_enabled"] = "true",
                ["hyperv_vm_count"] = "4",
                ["hyperv_running_count"] = "2",
            }
        };

        status.ExtractWindowsStatus();

        Assert.NotNull(status.HyperV);
        Assert.True(status.HyperV.Enabled);
        Assert.Equal(4, status.HyperV.VmCount);
        Assert.Equal(2, status.HyperV.RunningCount);
    }

    [Fact]
    public void ExtractWindowsStatus_HyperVDisabled()
    {
        var status = new AgentStatus
        {
            Metadata = new Dictionary<string, string>
            {
                ["hyperv_enabled"] = "false",
            }
        };

        status.ExtractWindowsStatus();

        Assert.Null(status.HyperV);
    }

    [Fact]
    public void ExtractWindowsStatus_HyperVMissing()
    {
        var status = new AgentStatus { Metadata = [] };

        status.ExtractWindowsStatus();

        Assert.Null(status.HyperV);
    }

    [Fact]
    public void ExtractWindowsStatus_WindowsUpdatePending()
    {
        var status = new AgentStatus
        {
            Metadata = new Dictionary<string, string>
            {
                ["windows_update_pending"] = "3",
                ["windows_update_reboot_required"] = "true",
            }
        };

        status.ExtractWindowsStatus();

        Assert.NotNull(status.WindowsUpdate);
        Assert.Equal(3, status.WindowsUpdate.PendingCount);
        Assert.True(status.WindowsUpdate.RebootRequired);
    }

    [Fact]
    public void ExtractWindowsStatus_NoUpdates()
    {
        var status = new AgentStatus { Metadata = [] };

        status.ExtractWindowsStatus();

        Assert.Null(status.WindowsUpdate);
    }

    [Fact]
    public void ExtractWindowsStatus_RebootOnlyNoCount()
    {
        var status = new AgentStatus
        {
            Metadata = new Dictionary<string, string>
            {
                ["windows_update_reboot_required"] = "true",
            }
        };

        status.ExtractWindowsStatus();

        Assert.NotNull(status.WindowsUpdate);
        Assert.Equal(0, status.WindowsUpdate.PendingCount);
        Assert.True(status.WindowsUpdate.RebootRequired);
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
