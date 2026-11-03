using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the one-screen setup: Hub URL and trust, token type and token,
/// and under Advanced the device ID and group ID. Connect enrolls the agent.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    private readonly AgentSettings _settings;
    private readonly ConnectionTester _connectionTester;
    private readonly Func<AgentSettings, bool, CancellationToken, Task<AgentConnectionAttemptResult>> _connectAgent;
    private CancellationTokenSource? _connectionAttempt;

    // Hub
    private string _hubUrl = "https://";
    private bool _isHubUrlValid;
    private string? _hubUrlError;

    // Token
    private bool _useEnrollmentToken = true;
    private string _token = string.Empty;
    private bool _isTokenValid;

    // Advanced, and the connection attempt
    private string _assetId = string.Empty;
    private string _groupId = string.Empty;
    private bool _isConnecting;
    private bool _isConnected;
    private string? _connectionError;

    // TLS trust — off by default. Must be explicitly enabled by the user for
    // self-signed homelab certificates. Flows into _settings.TlsSkipVerify on Connect.
    private bool _tlsSkipVerify;
    private string _tlsCaFile = string.Empty;

    public string HubUrl
    {
        get => _hubUrl;
        set
        {
            if (SetProperty(ref _hubUrl, value))
                OnHubUrlChanged(value);
        }
    }

    public bool IsHubUrlValid
    {
        get => _isHubUrlValid;
        set
        {
            if (SetProperty(ref _isHubUrlValid, value))
                OnPropertyChanged(nameof(CanConnect));
        }
    }

    public string? HubUrlError
    {
        get => _hubUrlError;
        set
        {
            if (SetProperty(ref _hubUrlError, value))
                OnPropertyChanged(nameof(HasHubUrlError));
        }
    }

    // The error line binds to this bool. Bound to the string through a
    // function, the empty line stayed visible after the URL was corrected.
    public bool HasHubUrlError => !string.IsNullOrEmpty(HubUrlError);

    public bool UseEnrollmentToken
    {
        get => _useEnrollmentToken;
        set
        {
            if (SetProperty(ref _useEnrollmentToken, value))
                OnUseEnrollmentTokenChanged(value);
        }
    }

    public string Token
    {
        get => _token;
        set
        {
            if (SetProperty(ref _token, value))
                OnTokenChanged(value);
        }
    }

    public bool IsTokenValid
    {
        get => _isTokenValid;
        set
        {
            if (SetProperty(ref _isTokenValid, value))
                OnPropertyChanged(nameof(CanConnect));
        }
    }

    public string AssetId
    {
        get => _assetId;
        set
        {
            if (SetProperty(ref _assetId, value))
                OnAssetIdChanged(value);
        }
    }

    public string GroupId
    {
        get => _groupId;
        set
        {
            if (SetProperty(ref _groupId, value))
                OnGroupIdChanged(value);
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (SetProperty(ref _isConnecting, value))
                OnIsConnectingChanged(value);
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string? ConnectionError
    {
        get => _connectionError;
        set
        {
            if (SetProperty(ref _connectionError, value))
                OnPropertyChanged(nameof(HasConnectionError));
        }
    }

    public bool HasConnectionError => !string.IsNullOrEmpty(ConnectionError);

    /// <summary>Footer text while Connect runs: "Connecting to hub.example.lan…".</summary>
    public string ConnectingText => $"Connecting to {AgentStatusText.HubHost(HubUrl)}…";

    public bool TlsSkipVerify
    {
        get => _tlsSkipVerify;
        set
        {
            if (SetProperty(ref _tlsSkipVerify, value))
                OnTlsSkipVerifyChanged(value);
        }
    }

    public string TlsCaFile
    {
        get => _tlsCaFile;
        set
        {
            if (SetProperty(ref _tlsCaFile, value))
                OnTlsCaFileChanged(value);
        }
    }

    public event Action? OnCompleted;

    public OnboardingViewModel(AgentSettings settings, CredentialStore credentialStore, ConnectionTester connectionTester)
        : this(
            settings,
            credentialStore,
            connectionTester,
            (_, _, _) => Task.FromResult(AgentConnectionAttemptResult.Failed(
                "Agent startup is unavailable.")))
    {
    }

    public OnboardingViewModel(
        AgentSettings settings,
        CredentialStore credentialStore,
        ConnectionTester connectionTester,
        Func<AgentSettings, bool, CancellationToken, Task<AgentConnectionAttemptResult>> connectAgent)
    {
        _settings = settings;
        ArgumentNullException.ThrowIfNull(credentialStore);
        _connectionTester = connectionTester;
        _connectAgent = connectAgent ?? throw new ArgumentNullException(nameof(connectAgent));
        HubUrl = settings.IsEnrolled ? settings.HubUrl : "https://";
        AssetId = string.IsNullOrWhiteSpace(settings.AssetId)
            ? Environment.MachineName
            : settings.AssetId;
        GroupId = settings.GroupId;
        TlsSkipVerify = settings.TlsSkipVerify;
        TlsCaFile = settings.TlsCaFile;
    }

    private void OnHubUrlChanged(string value)
    {
        ClearConnectionOutcome();
        IsHubUrlValid = SettingsValidator.IsValidHubUrl(value);
        // An empty field or the prefilled scheme is not an error yet.
        var untouched = value.Trim() is "" or "https://" or "http://" or "wss://" or "ws://";
        HubUrlError = IsHubUrlValid || untouched
            ? null
            : "Enter a full Hub URL that starts with https:// or wss://.";
        OnPropertyChanged(nameof(ConnectingText));
    }

    private void OnTokenChanged(string value)
    {
        ClearConnectionOutcome();
        IsTokenValid = SettingsValidator.IsValidToken(value);
    }

    private void OnUseEnrollmentTokenChanged(bool value)
    {
        ClearConnectionOutcome();
        OnPropertyChanged(nameof(UseApiToken));
        OnPropertyChanged(nameof(CanSetGroupId));
        // Group placement is a one-time enrollment request. An API token does
        // not create an asset-bound enrollment transaction, so retaining text
        // here would imply a placement change that the child intentionally
        // never sends.
        if (!value)
            GroupId = string.Empty;
    }

    /// <summary>
    /// Inverse binding used by the API-token radio button. Binding only the
    /// enrollment option leaves the view model stuck in enrollment mode when
    /// a user selects API Token.
    /// </summary>
    public bool UseApiToken
    {
        get => !UseEnrollmentToken;
        set
        {
            if (value)
                UseEnrollmentToken = false;
        }
    }

    public bool CanSetGroupId => UseEnrollmentToken;

    private void OnAssetIdChanged(string value) => ClearConnectionOutcome();

    private void OnGroupIdChanged(string value) => ClearConnectionOutcome();

    private void OnTlsSkipVerifyChanged(bool value) => ClearConnectionOutcome();

    private void OnTlsCaFileChanged(string value) => ClearConnectionOutcome();

    private void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(CanConnect));

    /// <summary>Connect needs a valid Hub URL and token, and no attempt already running.</summary>
    public bool CanConnect => IsHubUrlValid && IsTokenValid && !IsConnecting;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!CanConnect)
            return;
        IsConnecting = true;
        IsConnected = false;
        ConnectionError = null;
        var connectionAttempt = new CancellationTokenSource();
        _connectionAttempt = connectionAttempt;
        try
        {
            // Reachability is only a preflight. Setup is not successful until
            // the child subsequently reports authenticated connectivity.
            var probeResult = await _connectionTester.TestAsync(HubUrl, TlsSkipVerify, TlsCaFile);
            connectionAttempt.Token.ThrowIfCancellationRequested();
            if (!probeResult.Success)
            {
                ConnectionError = probeResult.Message;
                return;
            }

            // Setup edits an isolated candidate. AppState starts the exact Go
            // child against staged files and publishes this candidate only
            // after authenticated proof. The active settings and credentials
            // therefore remain untouched on rejection, cancellation, or any
            // persistence/start failure.
            var candidate = _settings.CloneForSetup();
            candidate.HubUrl = HubUrl;
            candidate.AssetId = AssetId.Trim();
            candidate.GroupId = UseEnrollmentToken ? GroupId.Trim() : string.Empty;
            candidate.TlsSkipVerify = TlsSkipVerify;
            candidate.TlsCaFile = TlsCaFile.Trim();
            candidate.LocalApiAuthToken = string.Empty;

            if (UseEnrollmentToken)
            {
                candidate.EnrollmentToken = Token.Trim();
                candidate.ApiToken = string.Empty;
            }
            else
            {
                candidate.ApiToken = Token.Trim();
                candidate.EnrollmentToken = string.Empty;
            }

            var connectionResult = await _connectAgent(
                candidate,
                UseEnrollmentToken,
                connectionAttempt.Token);
            if (!connectionResult.Success)
            {
                ConnectionError = connectionResult.Message;
                return;
            }

            IsConnected = true;
            OnCompleted?.Invoke();
        }
        catch (OperationCanceledException) when (connectionAttempt.IsCancellationRequested)
        {
            // The setup window was closed while the child was starting.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or PlatformNotSupportedException or
                                   System.Net.Sockets.SocketException or
                                   System.Security.Cryptography.CryptographicException or
                                   System.Security.SecurityException)
        {
            ConnectionError = "Could not start a secure connection to the Hub.";
        }
        finally
        {
            if (ReferenceEquals(_connectionAttempt, connectionAttempt))
                _connectionAttempt = null;
            connectionAttempt.Dispose();
            IsConnecting = false;
        }
    }

    public void CancelConnectionAttempt()
    {
        _connectionAttempt?.Cancel();
    }

    // A failure belongs to the exact values submitted. Any edit clears it, so
    // a retry shows only its own result.
    private void ClearConnectionOutcome()
    {
        ConnectionError = null;
        IsConnected = false;
    }
}
