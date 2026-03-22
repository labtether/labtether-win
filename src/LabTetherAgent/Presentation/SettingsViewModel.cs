using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the settings window. Two-way binds to AgentSettings.
/// Tracks dirty state and signals agent restart on save.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AgentSettings _settings;
    private readonly CredentialStore _credentialStore;

    [ObservableProperty] private string _hubUrl = string.Empty;
    [ObservableProperty] private bool _startAtLogin;
    [ObservableProperty] private bool _lowPowerMode;
    [ObservableProperty] private string _dockerEndpoint = string.Empty;
    [ObservableProperty] private string _dockerDiscoveryInterval = "30";
    [ObservableProperty] private bool _webRtcEnabled = true;
    [ObservableProperty] private bool _allowRemoteOverrides;
    [ObservableProperty] private bool _autoUpdateEnabled = true;
    [ObservableProperty] private bool _tlsSkipVerify;
    [ObservableProperty] private string _logLevel = "info";
    [ObservableProperty] private bool _isDirty;

    public event Action? OnSaved;
    public event Action? OnRestartRequired;

    public static readonly string[] LogLevels = ["debug", "info", "warn", "error"];

    public SettingsViewModel(AgentSettings settings, CredentialStore credentialStore)
    {
        _settings = settings;
        _credentialStore = credentialStore;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        HubUrl = _settings.HubUrl;
        StartAtLogin = _settings.StartAtLogin;
        LowPowerMode = _settings.LowPowerMode;
        DockerEndpoint = _settings.DockerEndpoint;
        DockerDiscoveryInterval = _settings.DockerDiscoveryInterval;
        WebRtcEnabled = _settings.WebRtcEnabled;
        AllowRemoteOverrides = _settings.AllowRemoteOverrides;
        AutoUpdateEnabled = _settings.AutoUpdateEnabled;
        TlsSkipVerify = _settings.TlsSkipVerify;
        LogLevel = _settings.LogLevel;
        IsDirty = false;
    }

    // Track changes via partial methods
    partial void OnHubUrlChanged(string value) => IsDirty = true;
    partial void OnStartAtLoginChanged(bool value) => IsDirty = true;
    partial void OnLowPowerModeChanged(bool value) => IsDirty = true;
    partial void OnDockerEndpointChanged(string value) => IsDirty = true;
    partial void OnDockerDiscoveryIntervalChanged(string value) => IsDirty = true;
    partial void OnWebRtcEnabledChanged(bool value) => IsDirty = true;
    partial void OnAllowRemoteOverridesChanged(bool value) => IsDirty = true;
    partial void OnAutoUpdateEnabledChanged(bool value) => IsDirty = true;
    partial void OnTlsSkipVerifyChanged(bool value) => IsDirty = true;
    partial void OnLogLevelChanged(string value) => IsDirty = true;

    [RelayCommand]
    private void Save()
    {
        _settings.HubUrl = HubUrl;
        _settings.StartAtLogin = StartAtLogin;
        _settings.LowPowerMode = LowPowerMode;
        _settings.DockerEndpoint = DockerEndpoint;
        _settings.DockerDiscoveryInterval = DockerDiscoveryInterval;
        _settings.WebRtcEnabled = WebRtcEnabled;
        _settings.AllowRemoteOverrides = AllowRemoteOverrides;
        _settings.AutoUpdateEnabled = AutoUpdateEnabled;
        _settings.TlsSkipVerify = TlsSkipVerify;
        _settings.LogLevel = LogLevel;

        _settings.Save();
        _credentialStore.SaveFrom(_settings);

        IsDirty = false;
        OnSaved?.Invoke();
        OnRestartRequired?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings();
    }
}
