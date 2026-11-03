using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Api;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the tray flyout: status, alerts, metrics and Open console.
/// </summary>
public partial class FlyoutViewModel : AgentStatusViewModel
{
    /// <summary>Saved Hub address and TLS trust, used by Open console.</summary>
    public AgentSettings? HubSettings { get; set; }

    /// <summary>Before setup there is no console to open.</summary>
    public bool CanOpenConsole => HubSettings?.IsEnrolled == true;

    public FlyoutViewModel(LocalApiClient apiClient) : base(apiClient) { }

    public void OnFlyoutOpened() => OnWindowOpened();

    public void OnFlyoutClosed() => OnWindowClosed();

    [RelayCommand(CanExecute = nameof(CanOpenConsole))]
    private async Task OpenConsoleAsync()
    {
        if (CanOpenConsole)
            await new ConsoleLauncher().OpenAsync(HubSettings!);
    }
}
