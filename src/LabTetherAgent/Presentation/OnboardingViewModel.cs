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
    private readonly CredentialStore _credentialStore;
    private readonly ConnectionTester _connectionTester;

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

    public event Action? OnCompleted;

    public OnboardingViewModel(AgentSettings settings, CredentialStore credentialStore, ConnectionTester connectionTester)
    {
        _settings = settings;
        _credentialStore = credentialStore;
        _connectionTester = connectionTester;
        _assetId = Environment.MachineName;
        UpdateNavigationState();
    }

    partial void OnHubUrlChanged(string value)
    {
        IsHubUrlValid = SettingsValidator.IsValidHubUrl(value);
        HubUrlError = IsHubUrlValid ? null : "Enter a valid hub URL (https:// or wss://)";
        UpdateNavigationState();
    }

    partial void OnTokenChanged(string value)
    {
        IsTokenValid = SettingsValidator.IsValidToken(value);
        UpdateNavigationState();
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateNavigationState();
    }

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
        ConnectionError = null;

        // Test connection
        var result = await _connectionTester.TestAsync(HubUrl);
        if (!result.Success)
        {
            ConnectionError = result.Message;
            IsConnecting = false;
            return;
        }

        // Save settings
        _settings.HubUrl = HubUrl;
        _settings.AssetId = AssetId.Trim();
        _settings.GroupId = GroupId.Trim();

        if (UseEnrollmentToken)
        {
            _settings.EnrollmentToken = Token.Trim();
            _settings.ApiToken = string.Empty;
        }
        else
        {
            _settings.ApiToken = Token.Trim();
            _settings.EnrollmentToken = string.Empty;
        }

        _settings.Save();
        _credentialStore.SaveFrom(_settings);

        IsConnecting = false;
        IsConnected = true;

        // Signal completion after a brief success display
        await Task.Delay(1500);
        OnCompleted?.Invoke();
    }

    private void UpdateNavigationState()
    {
        CanGoBack = CurrentStep > 1;
        CanGoNext = CurrentStep switch
        {
            1 => IsHubUrlValid,
            2 => IsTokenValid,
            _ => false
        };
    }
}
