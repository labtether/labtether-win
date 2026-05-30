using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LabTetherAgent.State;

namespace LabTetherAgent.Api;

/// <summary>
/// HTTP client for the Go agent's localhost API with ETag caching
/// and visibility-aware polling.
/// Mirrors mac-agent/Sources/LabTetherAgent/API/LocalAPIClient.swift.
/// </summary>
public class LocalApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string? _baseUrl;
    private string? _authToken;
    private string? _statusETag;
    private AgentStatusResponse? _cachedStatus;
    private Timer? _pollTimer;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly object _visibilityLock = new();
    private readonly SynchronizationContext? _callbackContext;
    private bool _manualVisible;
    private bool _isVisible;
    private int _visibleScopeCount;
    private int _failureCount;
    private TimeSpan _currentPollingInterval;
    private bool _disposed;

    private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    public event Action<AgentStatus>? OnStatusUpdated;
    public event Action<AgentInfoResponse>? OnInfoUpdated;
    public event Action<bool>? OnConnectionStateChanged; // true = connected
    public event Action<string>? OnError;

    public bool IsConnected { get; private set; }

    internal int FailureCount => _failureCount;
    internal TimeSpan CurrentPollingInterval => _currentPollingInterval;

    public LocalApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _callbackContext = SynchronizationContext.Current;
        _currentPollingInterval = NormalPollingInterval;
    }

    /// <summary>
    /// Configure the client with the agent's localhost URL and auth token.
    /// </summary>
    public void Configure(string port, string authToken)
    {
        _baseUrl = $"http://127.0.0.1:{port}";
        _authToken = authToken;
        _statusETag = null;
        _cachedStatus = null;
    }

    /// <summary>
    /// Start polling the agent status endpoint.
    /// </summary>
    public void StartPolling()
    {
        StopPolling();
        var interval = NormalPollingInterval;
        _currentPollingInterval = interval;
        _pollTimer = new Timer(async _ => await PollStatusAsync(), null, TimeSpan.Zero, interval);
    }

    /// <summary>
    /// Stop polling.
    /// </summary>
    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    /// <summary>
    /// Set visibility state. When visible (flyout/pop-out open), poll faster.
    /// </summary>
    public void SetVisible(bool visible)
    {
        lock (_visibilityLock)
        {
            _manualVisible = visible;
            ApplyVisibilityStateLocked();
        }
    }

    /// <summary>
    /// Mark one visible UI surface as active until the returned scope is disposed.
    /// Multiple windows can be visible at once, so each window owns its own scope.
    /// </summary>
    public IDisposable EnterVisibleScope()
    {
        lock (_visibilityLock)
        {
            _visibleScopeCount++;
            ApplyVisibilityStateLocked();
        }
        return new VisibleScope(this);
    }

    private void ExitVisibleScope()
    {
        lock (_visibilityLock)
        {
            if (_visibleScopeCount > 0)
                _visibleScopeCount--;
            ApplyVisibilityStateLocked();
        }
    }

    private void ApplyVisibilityStateLocked()
    {
        var visible = _manualVisible || _visibleScopeCount > 0;
        if (_isVisible == visible) return;
        _isVisible = visible;

        if (_pollTimer != null)
            ChangePollingInterval(TimeSpan.Zero, NormalPollingInterval); // poll immediately + reset interval
        else
            _currentPollingInterval = NormalPollingInterval;
    }

    /// <summary>
    /// Trigger an immediate poll (e.g., on network reconnect).
    /// </summary>
    public void PollNow()
    {
        _ = PollStatusAsync();
    }

    /// <summary>
    /// Fetch agent info (version, capabilities). Called once on startup.
    /// </summary>
    public async Task<AgentInfoResponse?> FetchInfoAsync()
    {
        if (_baseUrl == null) return null;

        try
        {
            var request = CreateRequest(HttpMethod.Get, "/agent/info");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<AgentInfoResponse>(json);
            if (info != null)
                RaiseOnCallbackContext(() => OnInfoUpdated?.Invoke(info));
            return info;
        }
        catch (Exception ex)
        {
            RaiseOnCallbackContext(() => OnError?.Invoke($"Failed to fetch agent info: {ex.Message}"));
            return null;
        }
    }

    private async Task PollStatusAsync()
    {
        if (_baseUrl == null || _disposed) return;
        if (!await _pollGate.WaitAsync(0)) return;

        try
        {
            var request = CreateRequest(HttpMethod.Get, "/agent/status");

            // ETag conditional request
            if (_statusETag != null)
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_statusETag));

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // Cache is still valid — update connection state but skip parsing
                SetConnected(true);
                ResetFailureBackoff();
                return;
            }

            response.EnsureSuccessStatusCode();

            // Save ETag for next request
            _statusETag = response.Headers.ETag?.Tag;

            var json = await response.Content.ReadAsStringAsync();
            _cachedStatus = JsonSerializer.Deserialize<AgentStatusResponse>(json);

            if (_cachedStatus != null)
            {
                var status = MapToAgentStatus(_cachedStatus);
                SetConnected(true);
                ResetFailureBackoff();
                RaiseOnCallbackContext(() => OnStatusUpdated?.Invoke(status));
            }
        }
        catch (Exception)
        {
            _failureCount++;
            SetConnected(false);

            // Apply exponential backoff on failure
            var backoff = TimeSpan.FromSeconds(
                Math.Min(5 * Math.Pow(2, _failureCount - 1), MaxBackoff.TotalSeconds));
            ChangePollingInterval(backoff, backoff);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    internal Task PollStatusForTestingAsync()
    {
        return PollStatusAsync();
    }

    private TimeSpan NormalPollingInterval => _isVisible ? VisibleInterval : HiddenInterval;

    private void ResetFailureBackoff()
    {
        _failureCount = 0;
        var interval = NormalPollingInterval;
        if (_currentPollingInterval != interval)
            ChangePollingInterval(interval, interval);
    }

    private void ChangePollingInterval(TimeSpan dueTime, TimeSpan period)
    {
        if (_disposed)
            return;

        _currentPollingInterval = period;
        _pollTimer?.Change(dueTime, period);
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        RaiseOnCallbackContext(() => OnConnectionStateChanged?.Invoke(connected));
    }

    private void RaiseOnCallbackContext(Action callback)
    {
        if (_callbackContext == null || SynchronizationContext.Current == _callbackContext)
        {
            callback();
            return;
        }

        _callbackContext.Post(_ => callback(), null);
    }

    private static AgentStatus MapToAgentStatus(AgentStatusResponse response)
    {
        var status = new AgentStatus
        {
            IsConnected = true,
            HubConnectionState = response.HubConnectionState,
            Uptime = response.Uptime,
            CpuPercent = response.CpuPercent,
            MemoryPercent = response.MemoryPercent,
            MemoryUsedBytes = response.MemoryUsedBytes,
            MemoryTotalBytes = response.MemoryTotalBytes,
            DiskPercent = response.DiskPercent,
            NetworkRxBytesPerSec = response.NetworkRxBytesPerSec,
            NetworkTxBytesPerSec = response.NetworkTxBytesPerSec,
            Metadata = response.Metadata ?? [],
            Alerts = response.Alerts?.Select(a =>
                new AlertSnapshot(a.Name, a.State, a.Severity, a.Message)).ToList() ?? [],
        };

        status.ExtractWindowsStatus();
        return status;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (_authToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        return request;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        _httpClient.Dispose();
    }

    private sealed class VisibleScope(LocalApiClient client) : IDisposable
    {
        private LocalApiClient? _client = client;

        public void Dispose()
        {
            var clientToRelease = Interlocked.Exchange(ref _client, null);
            clientToRelease?.ExitVisibleScope();
        }
    }
}
