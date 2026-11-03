using System.Net;
using System.Net.Http;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Api;

// "Offline since 21:12" and an alert's start time come from Go's RFC 3339
// times. A time the wrapper cannot read must never fail the whole status,
// or the tray would report a healthy agent as unreachable.
public class LocalApiClientTimeTests
{
    [Fact]
    public async Task GoNanosecondTimesMapAndUnreadableTimesAreIgnored()
    {
        var observed = await PollAsync(
            "{\"connected\":false,\"connection_state\":\"connecting\",\"disconnected_at\":\"2026-11-03T21:12:00.123456789Z\"," +
            "\"metrics\":{},\"alerts\":[" +
            "{\"title\":\"Disk is 91% full\",\"state\":\"firing\",\"severity\":\"warning\",\"timestamp\":\"2026-11-03T21:04:05.5Z\"}," +
            "{\"title\":\"Old\",\"state\":\"firing\",\"severity\":\"warning\",\"timestamp\":\"0001-01-01T00:00:00Z\"}," +
            "{\"title\":\"Odd\",\"state\":\"firing\",\"severity\":\"warning\",\"timestamp\":\"yesterday\"}]}");

        Assert.Equal(new DateTimeOffset(2026, 11, 3, 21, 12, 0, TimeSpan.Zero), TruncateToSecond(observed.DisconnectedAt));
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 21, 4, 5, TimeSpan.Zero), TruncateToSecond(observed.Alerts[0].Since));
        Assert.Null(observed.Alerts[1].Since);
        Assert.Null(observed.Alerts[2].Since);
    }

    [Fact]
    public async Task UnreadableDisconnectTimeStillDeliversStatus()
    {
        var observed = await PollAsync(
            "{\"connected\":false,\"connection_state\":\"connecting\",\"disconnected_at\":\"not a time\",\"metrics\":{\"cpu_percent\":7}}");

        Assert.Null(observed.DisconnectedAt);
        Assert.Equal(7, observed.CpuPercent);
    }

    private static async Task<AgentStatus> PollAsync(string json)
    {
        using var client = new LocalApiClient(new HttpClient(new JsonHandler(json)));
        AgentStatus? observed = null;
        client.OnStatusUpdated += status => observed = status;
        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();
        Assert.NotNull(observed);
        return observed!;
    }

    private static DateTimeOffset? TruncateToSecond(DateTimeOffset? value)
    {
        if (value is not { } v) return null;
        var u = v.ToUniversalTime();
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, u.Second, TimeSpan.Zero);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
