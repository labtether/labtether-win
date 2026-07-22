using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the 3-step onboarding wizard.
/// Step 1: Hub URL, Step 2: Token type + token, Step 3: Identity + connect.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    private readonly AgentSettings _settings;
    private readonly ConnectionTester _connectionTester;
    private readonly Func<AgentSettings, bool, CancellationToken, Task<AgentConnectionAttemptResult>> _connectAgent;
    private CancellationTokenSource? _connectionAttempt;

    [ObservableProperty] private int _currentStep = 1;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoBack;

    // Step 1
    [ObservableProperty] private string _hubUrl = "https://";
    [ObservableProperty] private bool _isHubUrlValid;
    [ObservableProperty] private string? _hubUrlError;

    // Step 2
    [ObservableProperty] private bool _useEnrollmentToken = true;
    [ObservableProperty] private string _token = string.Empty;
    [ObservableProperty] private bool _isTokenValid;

    // Step 3
    [ObservableProperty] private string _assetId = string.Empty;
    [ObservableProperty] private string _groupId = string.Empty;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _connectionError;

    // TLS trust — off by default. Must be explicitly enabled by the user for
    // self-signed homelab certificates. Flows into _settings.TlsSkipVerify on Finish.
    [ObservableProperty] private bool _tlsSkipVerify;
    [ObservableProperty] private string _tlsCaFile = string.Empty;

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
        UpdateNavigationState();
    }

    partial void OnHubUrlChanged(string value)
    {
        ClearConnectionOutcome();
        IsHubUrlValid = SettingsValidator.IsValidHubUrl(value);
        HubUrlError = IsHubUrlValid ? null : "Enter a valid hub URL (https:// or wss://)";
        UpdateNavigationState();
    }

    partial void OnTokenChanged(string value)
    {
        ClearConnectionOutcome();
        IsTokenValid = SettingsValidator.IsValidToken(value);
        UpdateNavigationState();
    }

    partial void OnUseEnrollmentTokenChanged(bool value)
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

    partial void OnAssetIdChanged(string value) => ClearConnectionOutcome();

    partial void OnGroupIdChanged(string value) => ClearConnectionOutcome();

    partial void OnTlsSkipVerifyChanged(bool value) => ClearConnectionOutcome();

    partial void OnTlsCaFileChanged(string value) => ClearConnectionOutcome();

    partial void OnCurrentStepChanged(int value)
    {
        // A failure belongs to the exact values submitted on the final step.
        // Do not carry that banner back through the wizard or show it again
        // when the user returns with corrected credentials.
        ClearConnectionOutcome();
        UpdateNavigationState();
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanFinish));
        UpdateNavigationState();
    }

    public bool CanFinish => !IsConnecting;

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep < 3) CurrentStep++;
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
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
            ConnectionError = "The agent core could not establish a secure connection. Check the agent logs and try again.";
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

    private void ClearConnectionOutcome()
    {
        ConnectionError = null;
        IsConnected = false;
    }

    private void UpdateNavigationState()
    {
        CanGoBack = CurrentStep > 1 && !IsConnecting;
        CanGoNext = CurrentStep switch
        {
            1 => IsHubUrlValid,
            2 => IsTokenValid,
            _ => false
        };
    }
}
