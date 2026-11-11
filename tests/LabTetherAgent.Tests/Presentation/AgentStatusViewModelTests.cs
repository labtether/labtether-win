using LabTetherAgent.Api;
using LabTetherAgent.Presentation;
using LabTetherAgent.Process;

namespace LabTetherAgent.Tests.Presentation;

// The pop-out is the status window people leave open. When the agent cannot
// start it must say "Stopped" and why, as the flyout does; it used to read
// "Offline" with the Hub's name and never showed the reason.
public class AgentStatusViewModelTests
{
    [Fact]
    public void PopOutSaysWhyTheAgentIsStoppedAndFollowsLaterErrors()
    {
        using var api = new LocalApiClient();
        using var process = new AgentProcess();
        process.ReportError("first problem");
        using var popOut = new PopOutViewModel(api) { HubUrl = "wss://hub.example.lan/ws/agent" };

        popOut.FollowAgentProcess(process);
        Assert.Equal("Stopped", popOut.StatusWord);
        Assert.Equal("first problem", popOut.Detail);

        process.ReportError("second problem");
        Assert.Equal("second problem", popOut.Detail);
    }
}
