using System.Globalization;
using System.Text.Json.Nodes;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Api;

/// <summary>
/// The app decodes every real <c>/agent/status</c> body from the protocol
/// contract with its own model and JSON options. Before this, the app's tests
/// used JSON written by hand, so the app and the agent could drift apart
/// with every test still green.
/// </summary>
public class StatusContractTests
{
    private static readonly string[] Expected =
    [
        "alerts-with-temperature",
        "auth-failed-enrollment-token-rejected",
        "auth-failed-token-rejected",
        "connected",
        "connected-token-persistence-failed",
        "connecting-transient-host-unreachable",
        "disconnected-no-transport",
        "enrollment-pending",
        "enrollment-refused-hub-words",
        "minimal-optional-fields-absent",
    ];

    // Fault: a fixture missing from the test output, so every test below
    // silently checks fewer states than the contract has.
    [Fact]
    public void EveryContractFixtureIsInTheTestOutput() =>
        Assert.Equal(Expected, StatusFixtures.Names);

    // Fault: a real body that the app cannot read, or reads into the wrong
    // fields. A failed decode counts as a failed poll, so the tray would show
    // a running agent as not answering.
    // Each body also carries fields the app does not read (agent_name, port,
    // metrics.temp_celsius, alert id), so a decoder that refuses unknown
    // fields from a newer agent fails here too.
    [Fact]
    public async Task EveryFixtureDecodesIntoTheFieldsTheAppShows()
    {
        foreach (var name in StatusFixtures.Names)
        {
            var json = StatusFixtures.Json(name);
            var (status, clientConnected) = await StatusFixtures.PollAsync(json.ToJsonString());

            Assert.True(json["connected"]!.GetValue<bool>() == status.IsConnected, $"{name}: connected");
            Assert.True(status.IsConnected == clientConnected, $"{name}: client connected");
            Assert.Equal(Text(json, "connection_state"), status.HubConnectionState);
            Assert.Equal(Text(json, "last_error"), status.LastError);
            Assert.Equal(Text(json, "last_error_reason"), status.LastErrorReason);
            Assert.Equal(Text(json, "enrollment_error"), status.EnrollmentError);
            Assert.Equal(json["uptime"]?.GetValue<string>(), status.Uptime);
            Assert.Equal(json["disconnected_at"] is null, status.DisconnectedAt is null);

            var metrics = json["metrics"]!;
            Assert.Equal(metrics["cpu_percent"]!.GetValue<double>(), status.CpuPercent);
            Assert.Equal(metrics["memory_percent"]!.GetValue<double>(), status.MemoryPercent);
            Assert.Equal(metrics["disk_percent"]!.GetValue<double>(), status.DiskPercent);
            Assert.Equal((long)Math.Round(metrics["net_rx_bytes_per_sec"]!.GetValue<double>()), status.NetworkRxBytesPerSec);
            Assert.Equal((long)Math.Round(metrics["net_tx_bytes_per_sec"]!.GetValue<double>()), status.NetworkTxBytesPerSec);

            var alerts = json["alerts"]!.AsArray();
            Assert.Equal(alerts.Count, status.Alerts.Count);
            for (var i = 0; i < alerts.Count; i++)
            {
                Assert.Equal(alerts[i]!["title"]!.GetValue<string>(), status.Alerts[i].Name);
                Assert.Equal(alerts[i]!["summary"]!.GetValue<string>(), status.Alerts[i].Message);
                Assert.Equal(alerts[i]!["state"]!.GetValue<string>(), status.Alerts[i].State);
                Assert.Equal(alerts[i]!["severity"]!.GetValue<string>(), status.Alerts[i].Severity);
                Assert.Equal(Instant(alerts[i]!["timestamp"]!.GetValue<string>()), status.Alerts[i].Since);
            }
        }
    }

    // Fault: Go writes up to nine fraction digits, drops trailing zeros, and
    // writes disconnected_at in the host's zone (+10:00). The Mac app failed
    // on this form. A time read wrong or not at all changes "Offline since".
    [Theory]
    [InlineData("2026-11-14T09:38:51.260417093Z")]
    [InlineData("2026-11-14T19:38:51.260417093+10:00")]
    [InlineData("2026-11-13T23:38:51.260417093-10:00")]
    [InlineData("2026-11-14T15:08:51.260417093+05:30")]
    public void DisconnectTimeInAnyZoneIsTheSameInstant(string sent)
    {
        var status = StatusFixtures.Decode(StatusFixtures.Variant(
            "connecting-transient-host-unreachable", body => body["disconnected_at"] = sent));

        var at = Assert.IsType<DateTimeOffset>(status.DisconnectedAt);
        var expected = new DateTimeOffset(2026, 11, 14, 9, 38, 51, TimeSpan.Zero).AddTicks(2_604_170);
        Assert.True(Math.Abs((at - expected).Ticks) <= 1, $"{sent} read as {at:O}");
    }

    [Theory]
    [InlineData("2026-11-14T09:38:51Z", 0)]
    [InlineData("2026-11-14T09:38:51.5Z", 5_000_000)]
    [InlineData("2026-11-14T09:38:51.000000001Z", 0)]
    [InlineData("2026-11-14T09:38:51.12+00:00", 1_200_000)]
    public void FractionsOfAnyLengthAreRead(string sent, long ticks)
    {
        var status = StatusFixtures.Decode(StatusFixtures.Variant(
            "connecting-transient-host-unreachable", body => body["disconnected_at"] = sent));

        var expected = new DateTimeOffset(2026, 11, 14, 9, 38, 51, TimeSpan.Zero).AddTicks(ticks);
        Assert.True(Math.Abs((status.DisconnectedAt!.Value - expected).Ticks) <= 1, $"{sent} read as {status.DisconnectedAt:O}");
    }

    // Fault: Go's zero time means "never"; shown, it would read as a date in
    // year 1. An unreadable time must not fail the whole status either.
    [Theory]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("yesterday")]
    [InlineData("2026-13-40T99:00:00Z")]
    public void ZeroOrUnreadableTimeIsUnknown(string sent)
    {
        var status = StatusFixtures.Decode(StatusFixtures.Variant(
            "connecting-transient-host-unreachable", body => body["disconnected_at"] = sent));

        Assert.Null(status.DisconnectedAt);
        Assert.Equal("transient", status.LastError);
    }

    // Fault: the contract gains a code, or the app loses one, and the app
    // shows only its generic line for it. Every reason must have words; every
    // last_error code has words or is one the app deliberately keeps quiet.
    [Fact]
    public void EveryContractCodeIsKnownToTheApp()
    {
        var codes = StatusFixtures.Codes;
        foreach (var reason in codes["last_error_reasons"]!.AsArray())
            Assert.True(AgentLastError.ReasonWords(reason!.GetValue<string>()) != null, $"no words for reason {reason}");

        string[] quiet = [AgentLastError.Transient, AgentLastError.UsedEnrollmentTokenKept];
        var lastErrors = codes["last_error_by_state"]!.AsObject()
            .SelectMany(state => state.Value!.AsArray().Select(code => code!.GetValue<string>()))
            .SelectMany(value => AgentLastError.Codes(value))
            .Distinct();
        foreach (var code in lastErrors)
            Assert.True(quiet.Contains(code) || AgentLastError.Words(code) != null, $"no words for last_error {code}");

        string[] states = ["connected", "connecting", "auth_failed", "disconnected"];
        Assert.Equal(states, codes["connection_states"]!.AsArray().Select(state => state!.GetValue<string>()));
    }

    private static string Text(JsonObject json, string field) => json[field]?.GetValue<string>() ?? string.Empty;

    private static DateTimeOffset Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
