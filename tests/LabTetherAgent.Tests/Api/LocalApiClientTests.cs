using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Api;

public class LocalApiClientTests
{
    [Fact]
    public async Task CurrentGoStatusContractMapsNestedMetricsAlertsAndConnectionState()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            request =>
            {
                Assert.Equal("/agent/status", request.RequestUri?.AbsolutePath);
                return CurrentGoStatusResponse(connected: true);
            })));
        AgentStatus? observed = null;
        client.OnStatusUpdated += status => observed = status;

        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();

        Assert.NotNull(observed);
        Assert.True(observed.IsConnected);
        Assert.Equal("connected", observed.HubConnectionState);
        Assert.Equal(string.Empty, observed.LastError);
        Assert.Equal(4.4140625, observed.CpuPercent);
        Assert.Equal(18, observed.MemoryPercent);
        Assert.Equal(72.4702089830618, observed.DiskPercent);
        Assert.Equal(13610, observed.NetworkRxBytesPerSec);
        Assert.Equal(837, observed.NetworkTxBytesPerSec);
        var alert = Assert.Single(observed.Alerts);
        Assert.Equal("Disk pressure", alert.Name);
        Assert.Equal("Free space is low", alert.Message);
    }

    [Fact]
    public async Task CurrentStatusMapsSanitizedEnrollmentRejectionForSetupUi()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"connected\":false,\"connection_state\":\"auth_failed\",\"last_error\":\"enrollment_token_rejected\",\"metrics\":{}}")
            })));
        AgentStatus? observed = null;
        client.OnStatusUpdated += status => observed = status;

        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();

        Assert.NotNull(observed);
        Assert.False(observed.IsConnected);
        Assert.Equal("auth_failed", observed.HubConnectionState);
        Assert.Equal("enrollment_token_rejected", observed.LastError);
    }

    [Fact]
    public async Task ReachableButHubDisconnectedAgentStaysDisconnectedWithoutPollFailure()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            _ => CurrentGoStatusResponse(connected: false))));

        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();

        Assert.False(client.IsConnected);
        Assert.Equal(0, client.FailureCount);
    }

    [Fact]
    public async Task CurrentConnectedBooleanIsAuthoritativeOverContradictoryStateText()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"connected\":false,\"connection_state\":\"connected\",\"metrics\":{}}")
            })));

        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task StoppingPollingClearsPreviouslyConnectedHubState()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            _ => CurrentGoStatusResponse(connected: true))));
        var connectionStates = new List<bool>();
        client.OnConnectionStateChanged += connectionStates.Add;

        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();
        Assert.True(client.IsConnected);

        client.StopPolling();

        Assert.False(client.IsConnected);
        Assert.Equal([true, false], connectionStates);
    }

    [Fact]
    public async Task ReconfigureIgnoresStatusCompletingFromReplacedChild()
    {
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new LocalApiClient(new HttpClient(new AsyncHttpHandler(
            _ => oldResponse.Task)));
        AgentStatus? observed = null;
        client.OnStatusUpdated += status => observed = status;

        client.Configure("18091", "old-local-token");
        var oldPoll = client.PollStatusForTestingAsync();

        client.Configure("18092", "new-local-token");
        oldResponse.SetResult(CurrentGoStatusResponse(connected: false));
        await oldPoll;

        Assert.Null(observed);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task FetchInfoUsesCurrentStatusContractForVersionAndFingerprint()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            request =>
            {
                Assert.Equal("/agent/status", request.RequestUri?.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("local-auth-token", request.Headers.Authorization?.Parameter);
                return CurrentGoStatusResponse(connected: true);
            })));
        client.Configure("18091", "local-auth-token");

        var info = await client.FetchInfoAsync();

        Assert.NotNull(info);
        Assert.Equal("qa-20260714-r8", info.Version);
        Assert.Equal("LT-TEST-FINGERPRINT", info.Fingerprint);
        Assert.False(info.UpdateAvailable);
    }

    [Fact]
    public async Task SuccessfulPollAfterFailuresRestoresVisiblePollingInterval()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(
            _ => throw new HttpRequestException("agent unavailable"),
            _ => throw new HttpRequestException("agent unavailable"),
            _ => StatusResponse())));

        client.Configure("8091", "token");
        client.SetVisible(true);

        await client.PollStatusForTestingAsync();
        await client.PollStatusForTestingAsync();

        Assert.Equal(2, client.FailureCount);
        Assert.Equal(TimeSpan.FromSeconds(10), client.CurrentPollingInterval);

        await client.PollStatusForTestingAsync();

        Assert.True(client.IsConnected);
        Assert.Equal(0, client.FailureCount);
        Assert.Equal(TimeSpan.FromSeconds(5), client.CurrentPollingInterval);
    }

    [Fact]
    public async Task NotModifiedPollAfterFailureResetsBackoff()
    {
        var notModifiedResponse = new TrackingHttpResponseMessage(HttpStatusCode.NotModified);
        var handler = new QueueHttpHandler(
            _ => StatusResponse(),
            _ => throw new HttpRequestException("agent unavailable"),
            request =>
            {
                Assert.Contains(request.Headers.IfNoneMatch, tag => tag.Tag == "\"status-1\"");
                return notModifiedResponse;
            });
        using var client = new LocalApiClient(new HttpClient(handler));

        client.Configure("8091", "token");

        await client.PollStatusForTestingAsync();
        await client.PollStatusForTestingAsync();

        Assert.Equal(1, client.FailureCount);
        Assert.Equal(TimeSpan.FromSeconds(5), client.CurrentPollingInterval);

        await client.PollStatusForTestingAsync();

        Assert.True(client.IsConnected);
        Assert.Equal(0, client.FailureCount);
        Assert.Equal(TimeSpan.FromSeconds(30), client.CurrentPollingInterval);
        Assert.True(notModifiedResponse.Disposed);
    }

    [Fact]
    public async Task PollStatusDisposesSuccessfulResponse()
    {
        var response = StatusResponse();
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler(_ => response)));

        client.Configure("8091", "token");

        await client.PollStatusForTestingAsync();

        Assert.True(response.Disposed);
    }

    [Fact]
    public void VisibleScopesKeepFastPollingUntilLastSurfaceCloses()
    {
        using var client = new LocalApiClient(new HttpClient(new QueueHttpHandler()));

        Assert.Equal(TimeSpan.FromSeconds(30), client.CurrentPollingInterval);

        var flyoutScope = client.EnterVisibleScope();
        Assert.Equal(TimeSpan.FromSeconds(5), client.CurrentPollingInterval);

        var popOutScope = client.EnterVisibleScope();
        Assert.Equal(TimeSpan.FromSeconds(5), client.CurrentPollingInterval);

        flyoutScope.Dispose();
        Assert.Equal(TimeSpan.FromSeconds(5), client.CurrentPollingInterval);

        popOutScope.Dispose();
        Assert.Equal(TimeSpan.FromSeconds(30), client.CurrentPollingInterval);

        popOutScope.Dispose();
        Assert.Equal(TimeSpan.FromSeconds(30), client.CurrentPollingInterval);
    }

    private static TrackingHttpResponseMessage StatusResponse()
    {
        var response = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "hub_connection_state": "connected",
                  "uptime": "1m",
                  "cpu_percent": 12.5,
                  "memory_percent": 40,
                  "memory_used_bytes": 1073741824,
                  "memory_total_bytes": 2147483648,
                  "disk_percent": 55,
                  "network_rx_bytes_per_sec": 100,
                  "network_tx_bytes_per_sec": 50,
                  "alerts": [],
                  "metadata": {}
                }
                """)
        };
        response.Headers.ETag = new EntityTagHeaderValue("\"status-1\"");
        return response;
    }

    private static TrackingHttpResponseMessage CurrentGoStatusResponse(bool connected)
    {
        var response = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "connected": {{connected.ToString().ToLowerInvariant()}},
                  "connection_state": "{{(connected ? "connected" : "disconnected")}}",
                  "uptime": "5h6m8s",
                  "device_fingerprint": "LT-TEST-FINGERPRINT",
                  "agent_version": "qa-20260714-r8",
                  "metrics": {
                    "cpu_percent": 4.4140625,
                    "memory_percent": 18,
                    "disk_percent": 72.4702089830618,
                    "net_rx_bytes_per_sec": 13610.026675652283,
                    "net_tx_bytes_per_sec": 836.6016397392139
                  },
                  "alerts": [
                    {
                      "title": "Disk pressure",
                      "summary": "Free space is low",
                      "severity": "warning",
                      "state": "firing"
                    }
                  ],
                  "update_available": false
                }
                """)
        };
        response.Headers.ETag = new EntityTagHeaderValue("\"go-status-1\"");
        return response;
    }

    private sealed class QueueHttpHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued HTTP response available.");

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class AsyncHttpHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }

    private sealed class TrackingHttpResponseMessage(HttpStatusCode statusCode) : HttpResponseMessage(statusCode)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
