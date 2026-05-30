using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using LabTetherAgent.Api;

namespace LabTetherAgent.Tests.Api;

public class LocalApiClientTests
{
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
