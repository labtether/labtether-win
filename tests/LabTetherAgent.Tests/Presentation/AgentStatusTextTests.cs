using System.Globalization;
using LabTetherAgent.Presentation;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Presentation;

// The flyout promises plain status words: "hub.example.lan · up 2 d 4 h" when
// connected, and a problem line with the reason and time when not. With no
// status at all (the agent is not answering) it must say Offline.
public class AgentStatusTextTests
{
    [Theory]
    [InlineData("52h14m0s", "2 d 4 h")]
    [InlineData("48h0m9s", "2 d")]
    [InlineData("4h12m3s", "4 h 12 min")]
    [InlineData("12m5s", "12 min")]
    [InlineData("45s", "45 s")]
    [InlineData("", "")]
    public void UptimeReadsAsDaysHoursAndMinutes(string goDuration, string expected) =>
        Assert.Equal(expected, AgentStatusText.FormatUptime(goDuration));

    // An agent that could not start reports no status. The flyout must then
    // say why it is stopped, not a bare "Offline".
    [Fact]
    public void HeaderSaysWhyTheAgentIsStopped()
    {
        const string hub = "wss://hub.example.lan/ws/agent";
        var problem = LabTetherAgent.Services.HubCertificateTrust.SavedCaUnusable;

        Assert.Equal(new AgentHeader("Stopped", problem, true),
            AgentStatusText.Header(null, hub, DateTimeOffset.Now, problem));
        Assert.Equal("Connected",
            AgentStatusText.Header(new AgentStatus { IsConnected = true }, hub, DateTimeOffset.Now, problem).StatusWord);
    }

    [Fact]
    public void HeaderShowsHostAndUptimeOrTheProblem()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var now = DateTimeOffset.Now;
            var at = new DateTimeOffset(now.Year, now.Month, now.Day, 21, 12, 0, now.Offset);
            const string hub = "wss://hub.example.lan:8443/ws/agent";

            Assert.Equal(new AgentHeader("Connected", "hub.example.lan · up 2 d 4 h", false),
                AgentStatusText.Header(new AgentStatus { IsConnected = true, Uptime = "52h14m0s" }, hub, now));
            Assert.Equal(new AgentHeader("Offline", "The Hub did not answer in time · since 21:12", true),
                AgentStatusText.Header(new AgentStatus { LastErrorReason = "timed_out", DisconnectedAt = at }, hub, now));
            Assert.Equal(new AgentHeader("Connecting", "hub.example.lan", false),
                AgentStatusText.Header(new AgentStatus { HubConnectionState = "connecting" }, hub, now));
            Assert.Equal(new AgentHeader("Offline", "hub.example.lan", true), AgentStatusText.Header(null, hub, now));
            // Before setup the flyout showed the placeholder Hub "localhost" as offline.
            Assert.Equal(new AgentHeader("Not set up", "", false), AgentStatusText.Header(null, "", now));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // enrollment_pending is a normal step: say what to do, not "Connecting".
    [Fact]
    public void HeaderAsksForApprovalWhileEnrollmentWaits()
    {
        var pending = new AgentStatus { HubConnectionState = "connecting", LastError = "enrollment_pending" };

        Assert.Equal(new AgentHeader("Waiting for approval", "Approve this device in Devices on the Hub", false),
            AgentStatusText.Header(pending, "wss://hub.example.lan/ws/agent", DateTimeOffset.Now));
    }

    // Connected, but the agent token was not saved: the device loses its
    // Hub at the next start, so the header must say so. The agent may join a
    // second code after a comma (run.go).
    [Theory]
    [InlineData("agent_token_persistence_failed")]
    [InlineData("agent_token_persistence_failed,consumed_enrollment_token_cleanup_failed")]
    public void HeaderWarnsWhenTheAgentTokenWasNotSaved(string lastError)
    {
        var status = new AgentStatus
        {
            IsConnected = true, HubConnectionState = "connected", LastError = lastError, Uptime = "45s",
        };

        Assert.Equal(new AgentHeader("Connected",
                "hub.example.lan · Windows could not save the agent token. View logs shows why.", true),
            AgentStatusText.Header(status, "wss://hub.example.lan/ws/agent", DateTimeOffset.Now));
    }

    [Fact]
    public void HeaderNamesTheHostOfAnAddressWithoutScheme() =>
        Assert.Equal("hub.example.lan", AgentStatusText.HubHost("hub.example.lan:8443"));

    // No code the agent sends today, or a new one it may send later, reaches
    // the screen as it is.
    [Fact]
    public void NoAgentCodeIsShownAsText()
    {
        string[] states = ["connected", "connecting", "auth_failed", "disconnected", "", "a_future_state"];
        string[] errors =
        [
            "", "transient", "auth_failed", "enrollment_pending", "enrollment_token_rejected",
            "agent_token_persistence_failed", "consumed_enrollment_token_cleanup_failed",
            "agent_token_persistence_failed,consumed_enrollment_token_cleanup_failed", "a_future_code",
        ];
        string[] reasons =
        [
            "", "name_lookup_failed", "connection_refused", "host_unreachable", "timed_out",
            "certificate_untrusted", "token_rejected", "a_future_reason",
        ];
        foreach (var state in states)
        foreach (var error in errors)
        foreach (var reason in reasons)
        {
            var status = new AgentStatus
            {
                IsConnected = state == "connected", HubConnectionState = state, LastError = error, LastErrorReason = reason,
            };
            var header = AgentStatusText.Header(status, "wss://hub.example.lan/ws/agent", DateTimeOffset.Now);
            var tray = TrayStatus.Resolve(status.IsConnected, status, null, null, DateTimeOffset.Now);
            foreach (var text in new[] { header.StatusWord, header.Detail, tray.Tooltip })
                Assert.DoesNotContain("_", text);
        }
    }
}
