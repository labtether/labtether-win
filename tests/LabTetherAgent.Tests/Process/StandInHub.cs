using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace LabTetherAgent.Tests.Process;

/// <summary>
/// A loopback stand-in for the Hub's agent WebSocket. It accepts the first
/// /ws/agent upgrade and records how that connection ended: a close frame,
/// or the socket dropping without one. Other requests get 404.
/// </summary>
internal sealed class StandInHub : IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StandInHub(int firstPort, int lastPort)
    {
        Port = FreePort(firstPort, lastPort);
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port { get; }
    public Task Connected => _connected.Task;
    public Task<string> Ended => _ended.Task;

    /// <summary>The first loopback port in the range that nothing listens on.</summary>
    public static int FreePort(int first, int last)
    {
        for (var port = first; port <= last; port++)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
                probe.Start();
                probe.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        throw new InvalidOperationException($"No free loopback port in {first}-{last}.");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var head = await ReadHeadAsync(stream);
        var key = Header(head, "Sec-WebSocket-Key");
        if (key == null || !head.StartsWith("GET /ws/agent", StringComparison.Ordinal) || _connected.Task.IsCompleted)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            return;
        }
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n"));
        using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
        _connected.TrySetResult();
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer, _stop.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    _ended.TrySetResult($"close frame {(int?)socket.CloseStatus} {socket.CloseStatusDescription}".Trim());
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException)
        {
            _ended.TrySetResult("connection dropped without a close frame");
        }
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 16 * 1024 && await stream.ReadAsync(one) == 1)
        {
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string? Header(string head, string name) =>
        head.Split("\r\n").Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _ended.TrySetResult("stand-in Hub stopped first");
    }
}
