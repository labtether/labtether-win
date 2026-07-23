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

    private string _hubUrl = string.Empty;
    private bool _startAtLogin;
    private bool _lowPowerMode;
    private string _dockerEndpoint = string.Empty;
    private string _dockerDiscoveryInterval = "30";
    private bool _webRtcEnabled = true;
    private bool _allowRemoteOverrides;
    private bool _tlsSkipVerify;
    private string _tlsCaFile = string.Empty;
    private string _logLevel = "info";
    private bool _isDirty;
    private string? _saveError;

    public string HubUrl
    {
        get => _hubUrl;
        set
        {
            if (SetProperty(ref _hubUrl, value))
                OnHubUrlChanged(value);
        }
    }

    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (SetProperty(ref _startAtLogin, value))
                OnStartAtLoginChanged(value);
        }
    }

    public bool LowPowerMode
    {
        get => _lowPowerMode;
        set
        {
            if (SetProperty(ref _lowPowerMode, value))
                OnLowPowerModeChanged(value);
        }
    }

    public string DockerEndpoint
    {
        get => _dockerEndpoint;
        set
        {
            if (SetProperty(ref _dockerEndpoint, value))
                OnDockerEndpointChanged(value);
        }
    }

    public string DockerDiscoveryInterval
    {
        get => _dockerDiscoveryInterval;
        set
        {
            if (SetProperty(ref _dockerDiscoveryInterval, value))
                OnDockerDiscoveryIntervalChanged(value);
        }
    }

    public bool WebRtcEnabled
    {
        get => _webRtcEnabled;
        set
        {
            if (SetProperty(ref _webRtcEnabled, value))
                OnWebRtcEnabledChanged(value);
        }
    }

    public bool AllowRemoteOverrides
    {
        get => _allowRemoteOverrides;
        set
        {
            if (SetProperty(ref _allowRemoteOverrides, value))
                OnAllowRemoteOverridesChanged(value);
        }
    }

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

    public string LogLevel
    {
        get => _logLevel;
        set
        {
            if (SetProperty(ref _logLevel, value))
                OnLogLevelChanged(value);
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public string? SaveError
    {
        get => _saveError;
        set => SetProperty(ref _saveError, value);
    }

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
    private void OnHubUrlChanged(string value) => IsDirty = true;
    private void OnStartAtLoginChanged(bool value) => IsDirty = true;
    private void OnLowPowerModeChanged(bool value) => IsDirty = true;
    private void OnDockerEndpointChanged(string value) => IsDirty = true;
    private void OnDockerDiscoveryIntervalChanged(string value) => IsDirty = true;
    private void OnWebRtcEnabledChanged(bool value) => IsDirty = true;
    private void OnAllowRemoteOverridesChanged(bool value) => IsDirty = true;
    private void OnTlsSkipVerifyChanged(bool value) => IsDirty = true;
    private void OnTlsCaFileChanged(string value) => IsDirty = true;
    private void OnLogLevelChanged(string value) => IsDirty = true;

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
        _settings.AutoUpdateEnabled = false;
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
