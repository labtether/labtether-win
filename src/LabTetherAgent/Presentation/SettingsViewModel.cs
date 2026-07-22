using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.App;
using LabTetherAgent.Services;
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
    private readonly ILoginItemManager _loginItemManager;
    private readonly Action<AgentSettings, CredentialStore> _persistSettings;

    [ObservableProperty] private string _hubUrl = string.Empty;
    [ObservableProperty] private bool _startAtLogin;
    [ObservableProperty] private bool _lowPowerMode;
    [ObservableProperty] private string _dockerEndpoint = string.Empty;
    [ObservableProperty] private string _dockerDiscoveryInterval = "30";
    [ObservableProperty] private bool _webRtcEnabled = true;
    [ObservableProperty] private bool _allowRemoteOverrides;
    [ObservableProperty] private bool _autoUpdateEnabled = true;
    [ObservableProperty] private bool _tlsSkipVerify;
    [ObservableProperty] private string _tlsCaFile = string.Empty;
    [ObservableProperty] private string _logLevel = "info";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _saveError;

    public event Action? OnSaved;
    public event Action? OnRestartRequired;

    public readonly string[] LogLevels = ["debug", "info", "warn", "error"];

    public SettingsViewModel(AgentSettings settings, CredentialStore credentialStore)
        : this(
            settings,
            credentialStore,
            new LoginItemManager(),
            static (currentSettings, currentCredentialStore) =>
            {
                currentSettings.Save();
                currentCredentialStore.SaveFrom(currentSettings);
            })
    {
    }

    internal SettingsViewModel(
        AgentSettings settings,
        CredentialStore credentialStore,
        ILoginItemManager loginItemManager,
        Action<AgentSettings, CredentialStore> persistSettings)
    {
        _settings = settings;
        _credentialStore = credentialStore;
        _loginItemManager = loginItemManager;
        _persistSettings = persistSettings;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        HubUrl = _settings.HubUrl;
        StartAtLogin = _loginItemManager.IsEnabled();
        LowPowerMode = _settings.LowPowerMode;
        DockerEndpoint = _settings.DockerEndpoint;
        DockerDiscoveryInterval = _settings.DockerDiscoveryInterval;
        WebRtcEnabled = _settings.WebRtcEnabled;
        AllowRemoteOverrides = _settings.AllowRemoteOverrides;
        AutoUpdateEnabled = _settings.AutoUpdateEnabled;
        TlsSkipVerify = _settings.TlsSkipVerify;
        TlsCaFile = _settings.TlsCaFile;
        LogLevel = _settings.LogLevel;
        IsDirty = false;
        SaveError = null;
    }

    /// <summary>
    /// Numeric accessor for DockerDiscoveryInterval, used by NumberBox x:Bind.
    /// </summary>
    public double DockerDiscoveryIntervalNumber
    {
        get => SettingsValidator.TryParseIntegerInRange(DockerDiscoveryInterval, 10, 600, out var seconds)
            ? seconds
            : 30;
        set
        {
            var seconds = double.IsFinite(value)
                ? Math.Clamp((int)Math.Round(value), 10, 600)
                : 30;
            DockerDiscoveryInterval = seconds.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged();
        }
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
    partial void OnTlsCaFileChanged(string value) => IsDirty = true;
    partial void OnLogLevelChanged(string value) => IsDirty = true;

    [RelayCommand]
    private void Save()
    {
        SaveError = null;
        if (!SettingsValidator.IsValidHubUrl(HubUrl))
        {
            SaveError = "Enter a valid hub URL using https://, wss://, http://, or ws://.";
            return;
        }
        if (!SettingsValidator.IsValidLogLevel(LogLevel))
        {
            SaveError = "Select a valid log level.";
            return;
        }
        if (ConnectionTester.HasConflictingTlsTrustOptions(TlsSkipVerify, TlsCaFile))
        {
            SaveError = "Choose either a custom CA certificate or skip certificate verification, not both.";
            return;
        }
        if (!ConnectionTester.TryNormalizeCustomCaFile(TlsCaFile, out var normalizedCaFile)
            || (normalizedCaFile.Length > 0
                && !ConnectionTester.IsUsableCustomCaFile(normalizedCaFile)))
        {
            SaveError = "Choose a readable, regular custom CA certificate file.";
            return;
        }
        if (normalizedCaFile.Length > 0
            && !ConnectionTester.IsValidCustomCaFile(normalizedCaFile))
        {
            SaveError = "Choose a valid PEM custom CA file containing a self-signed root certificate.";
            return;
        }

        if (!_loginItemManager.SetEnabled(StartAtLogin))
        {
            SaveError = "Windows could not update Start at Login. No settings were saved.";
            return;
        }

        _settings.HubUrl = HubUrl.Trim();
        _settings.StartAtLogin = _loginItemManager.IsEnabled();
        _settings.LowPowerMode = LowPowerMode;
        _settings.DockerEndpoint = DockerEndpoint;
        _settings.DockerDiscoveryInterval =
            SettingsValidator.NormalizeDockerDiscoveryInterval(DockerDiscoveryInterval);
        _settings.WebRtcEnabled = WebRtcEnabled;
        _settings.AllowRemoteOverrides = AllowRemoteOverrides;
        _settings.AutoUpdateEnabled = AutoUpdateEnabled;
        _settings.TlsSkipVerify = TlsSkipVerify;
        _settings.TlsCaFile = normalizedCaFile;
        _settings.LogLevel = LogLevel;

        _persistSettings(_settings, _credentialStore);

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
