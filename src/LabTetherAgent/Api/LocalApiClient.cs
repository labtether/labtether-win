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
    private readonly Func<Task>? _beforeStatusCommit;
    private string? _baseUrl;
    private string? _authToken;
    private string? _statusETag;
    private AgentStatusResponse? _cachedStatus;
    private Timer? _pollTimer;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly SynchronizationContext? _callbackContext;
    private bool _manualVisible;
    private bool _isVisible;
    private int _visibleScopeCount;
    private int _failureCount;
    private TimeSpan _currentPollingInterval;
    private long _configurationVersion;
    private bool _disposed;

    private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    public event Action<AgentStatus>? OnStatusUpdated;
    public event Action<AgentInfoResponse>? OnInfoUpdated;
    public event Action<bool>? OnConnectionStateChanged; // true = connected
    public event Action<string>? OnError;

    private bool _isConnected;
    public bool IsConnected { get { lock (_stateGate) return _isConnected; } }

    internal int FailureCount { get { lock (_stateGate) return _failureCount; } }
    internal TimeSpan CurrentPollingInterval { get { lock (_stateGate) return _currentPollingInterval; } }

    public LocalApiClient(HttpClient? httpClient = null) : this(httpClient, null) { }

    internal LocalApiClient(HttpClient? httpClient, Func<Task>? beforeStatusCommit)
    {
        _beforeStatusCommit = beforeStatusCommit;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _callbackContext = SynchronizationContext.Current;
        _currentPollingInterval = NormalPollingInterval;
    }

    /// <summary>
    /// Configure the client with the agent's localhost URL and auth token.
    /// </summary>
    public void Configure(string port, string authToken) => ConfigureState(port, authToken)();

    internal Action ConfigureState(string port, string authToken)
    {
        lock (_stateGate)
        {
            if (_disposed) return () => { };
            var notify = StopPollingState();
            _baseUrl = $"http://127.0.0.1:{port}";
            _authToken = authToken;
            return notify;
        }
    }

    /// <summary>
    /// Start polling the agent status endpoint.
    /// </summary>
    public void StartPolling() => StartPollingState()();

    internal Action StartPollingState()
    {
        lock (_stateGate)
        {
            var notify = StopPollingState();
            if (_disposed) return notify;
            var interval = NormalPollingInterval;
            _currentPollingInterval = interval;
            var version = _configurationVersion;
            _pollTimer = new Timer(async _ => await PollStatusAsync(version), null, TimeSpan.Zero, interval);
            return notify;
        }
    }

    public void StopPolling() => StopPollingState()();

    // State commits are atomic with Configure; the returned notification runs
    // after both this state gate and AppState's lifecycle gate are released.
    internal Action StopPollingState()
    {
        lock (_stateGate)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _configurationVersion++;
            _statusETag = null;
            _cachedStatus = null;
            ResetFailureBackoff();
            return SetConnectedState(false, _configurationVersion);
        }
    }

    /// <summary>
    /// Set visibility state. When visible (flyout/pop-out open), poll faster.
    /// </summary>
    public void SetVisible(bool visible)
    {
        lock (_stateGate)
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
        lock (_stateGate)
        {
            _visibleScopeCount++;
            ApplyVisibilityStateLocked();
        }
        return new VisibleScope(this);
    }

    private void ExitVisibleScope()
    {
        lock (_stateGate)
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
        long version;
        lock (_stateGate)
        {
            if (_disposed || _pollTimer == null) return;
            version = _configurationVersion;
        }
        _ = PollStatusAsync(version);
    }

    /// <summary>
    /// Fetch agent info (version, capabilities). Called once on startup.
    /// </summary>
    public async Task<AgentInfoResponse?> FetchInfoAsync()
    {
        long configurationVersion;
        string? baseUrl, authToken;
        lock (_stateGate)
        {
            configurationVersion = _configurationVersion;
            baseUrl = _baseUrl;
            authToken = _authToken;
            if (baseUrl == null || _disposed) return null;
        }

        try
        {
            // The Go child exposes wrapper-facing version, fingerprint, and
            // update fields on /agent/status. /agent/info is only its generic
            // endpoint-helper health payload (os/mode/status).
            using var request = CreateRequest(HttpMethod.Get, "/agent/status", baseUrl, authToken);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var status = JsonSerializer.Deserialize<AgentStatusResponse>(json);
            var info = status == null
                ? null
                : new AgentInfoResponse
                {
                    Version = status.AgentVersion,
                    Fingerprint = status.DeviceFingerprint,
                    UpdateAvailable = status.UpdateAvailable,
                    UpdateVersion = status.LatestVersion,
                };
            lock (_stateGate)
            {
                if (_disposed || configurationVersion != _configurationVersion) return null;
            }
            if (info != null)
                RaiseOnCallbackContext(() => OnInfoUpdated?.Invoke(info), configurationVersion);
            return info;
        }
        catch (Exception ex)
        {
            lock (_stateGate)
            {
                if (_disposed || configurationVersion != _configurationVersion) return null;
            }
            RaiseOnCallbackContext(
                () => OnError?.Invoke($"Failed to fetch agent info: {ex.Message}"), configurationVersion);
            return null;
        }
    }

    private async Task PollStatusAsync(long? expectedVersion = null)
    {
        if (!await _pollGate.WaitAsync(0)) return;
        try
        {
            long configurationVersion;
            string? baseUrl, authToken, etag;
            lock (_stateGate)
            {
                configurationVersion = _configurationVersion;
                baseUrl = _baseUrl;
                authToken = _authToken;
                etag = _statusETag;
                if (baseUrl == null || _disposed ||
                    (expectedVersion.HasValue && expectedVersion != _configurationVersion)) return;
            }
            AgentStatusResponse? decoded = null;
            AgentStatus? status = null;
            string? responseETag = null;
            bool notModified = false, failed = false;
            try
            {
                using var request = CreateRequest(HttpMethod.Get, "/agent/status", baseUrl, authToken);
                if (etag != null) request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
                using var response = await _httpClient.SendAsync(request);
                notModified = response.StatusCode == HttpStatusCode.NotModified;
                if (!notModified)
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    decoded = JsonSerializer.Deserialize<AgentStatusResponse>(json);
                    if (decoded != null) status = MapToAgentStatus(decoded);
                    responseETag = response.Headers.ETag?.Tag;
                }
            }
            catch (Exception) { failed = true; }

            // A test can hold an actual completed request at the commit boundary.
            if (_beforeStatusCommit != null) await _beforeStatusCommit();
            Action notify;
            lock (_stateGate)
            {
                if (_disposed || configurationVersion != _configurationVersion) return;
                if (failed)
                {
                    _failureCount++;
                    notify = SetConnectedState(false, configurationVersion);
                    var backoff = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, _failureCount - 1), MaxBackoff.TotalSeconds));
                    ChangePollingInterval(backoff, backoff);
                }
                else
                {
                    if (!notModified)
                    {
                        _cachedStatus = decoded;
                        _statusETag = responseETag;
                    }
                    notify = SetConnectedState(_cachedStatus?.IsHubConnected ?? false, configurationVersion);
                    ResetFailureBackoff();
                }
            }
            notify();
            if (!failed && status != null)
                RaiseOnCallbackContext(() => OnStatusUpdated?.Invoke(status), configurationVersion);
        }
        finally { _pollGate.Release(); }
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

    // Caller holds _stateGate. Never invoke subscribers during a state commit.
    private Action SetConnectedState(bool connected, long version)
    {
        var changed = _isConnected != connected;
        _isConnected = connected;
        return () =>
        {
            if (changed) RaiseOnCallbackContext(() => OnConnectionStateChanged?.Invoke(connected), version);
        };
    }

    private void RaiseOnCallbackContext(Action callback, long configurationVersion)
    {
        void InvokeIfCurrent()
        {
            lock (_stateGate) { if (_disposed || configurationVersion != _configurationVersion) return; }
            callback();
        }

        if (_callbackContext == null || SynchronizationContext.Current == _callbackContext)
        {
            InvokeIfCurrent();
            return;
        }

        _callbackContext.Post(_ => InvokeIfCurrent(), null);
    }

    private static AgentStatus MapToAgentStatus(AgentStatusResponse response)
    {
        var status = new AgentStatus
        {
            IsConnected = response.IsHubConnected,
            HubConnectionState = response.EffectiveConnectionState,
            LastError = response.LastError,
            LastErrorReason = response.LastErrorReason ?? string.Empty,
            Uptime = response.Uptime,
            CpuPercent = response.Metrics?.CpuPercent ?? response.CpuPercent,
            MemoryPercent = response.Metrics?.MemoryPercent ?? response.MemoryPercent,
            MemoryUsedBytes = response.MemoryUsedBytes,
            MemoryTotalBytes = response.MemoryTotalBytes,
            DiskPercent = response.Metrics?.DiskPercent ?? response.DiskPercent,
            NetworkRxBytesPerSec = (long)Math.Round(
                response.Metrics?.NetworkRxBytesPerSec ?? response.NetworkRxBytesPerSec),
            NetworkTxBytesPerSec = (long)Math.Round(
                response.Metrics?.NetworkTxBytesPerSec ?? response.NetworkTxBytesPerSec),
            Metadata = response.Metadata ?? [],
            Alerts = response.Alerts?.Select(a =>
                new AlertSnapshot(
                    string.IsNullOrWhiteSpace(a.Name) ? a.Title ?? string.Empty : a.Name,
                    a.State ?? string.Empty,
                    a.Severity ?? string.Empty,
                    a.Message ?? a.Summary)).ToList() ?? [],
        };

        return status;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string baseUrl,
        string? authToken)
    {
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        if (authToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        return request;
    }

    public void Dispose()
    {
        Action notify;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            notify = StopPollingState();
        }
        notify();
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
