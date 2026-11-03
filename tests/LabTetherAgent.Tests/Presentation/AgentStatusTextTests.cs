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
            Assert.Equal(new AgentHeader("Offline", "Can't reach the Hub · since 21:12", true),
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
}
