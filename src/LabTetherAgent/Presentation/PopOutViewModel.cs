using LabTetherAgent.Api;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the pop-out (always-on-top) window. Shares the flyout's status
/// data in a window that stays open.
/// </summary>
public class PopOutViewModel : AgentStatusViewModel
{
    public PopOutViewModel(LocalApiClient apiClient) : base(apiClient) { }
}
