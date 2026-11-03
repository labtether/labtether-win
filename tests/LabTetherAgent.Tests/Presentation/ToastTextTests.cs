using System.Globalization;
using LabTetherAgent.Presentation;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Presentation;

// Notifications promise a status word plus a time, with the same time the tray
// tooltip shows: the agent's own disconnect or alert start time when it sends
// one. An alert with no message still gets a body line instead of an empty one.
public class ToastTextTests
{
    [Fact]
    public void OfflineToastUsesTheAgentsDisconnectTimeWhenItHasOne()
    {
        WithInvariantCulture(now =>
        {
            var dropped = At(now, 21, 12);
            Assert.Equal(new ToastMessage("Offline since 21:12", "Lost the connection to the Hub. Reconnecting."),
                ToastText.Disconnected(new AgentStatus { DisconnectedAt = dropped }, At(now, 21, 14)));
            // A stale connected status (the agent stopped answering) has no drop time.
            Assert.Equal("Offline since 21:14",
                ToastText.Disconnected(new AgentStatus { IsConnected = true, DisconnectedAt = dropped }, At(now, 21, 14)).Title);
            Assert.Equal("Offline since 21:14", ToastText.Disconnected(null, At(now, 21, 14)).Title);
        });
    }

    [Theory]
    [InlineData("critical", "Disk is 91% full", true, "Critical alert: Disk C:", "Disk is 91% full · since 21:04")]
    [InlineData("warning", "", true, "Alert: Disk C:", "Firing since 21:04")]
    [InlineData("warning", "Disk is 91% full", false, "Alert: Disk C:", "Disk is 91% full")]
    [InlineData("warning", null, false, "Alert: Disk C:", "Firing")]
    public void AlertToastNamesTheAlertAndWhenItStarted(
        string severity, string? message, bool hasSince, string title, string body)
    {
        WithInvariantCulture(now =>
        {
            var alert = new AlertSnapshot("Disk C:", "firing", severity, message, hasSince ? At(now, 21, 4) : null);
            Assert.Equal(new ToastMessage(title, body), ToastText.Alert(alert, now));
        });
    }

    private static DateTimeOffset At(DateTimeOffset day, int hour, int minute) =>
        new(day.Year, day.Month, day.Day, hour, minute, 0, day.Offset);

    private static void WithInvariantCulture(Action<DateTimeOffset> check)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { check(DateTimeOffset.Now); }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
