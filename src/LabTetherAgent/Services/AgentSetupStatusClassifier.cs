using LabTetherAgent.State;

namespace LabTetherAgent.Services;

internal static class AgentSetupStatusClassifier
{
    public static string? TerminalFailureMessage(AgentStatus status)
    {
        // The Hub's own words (for example a device ID that is already
        // enrolled) say more than any fixed text here.
        var hubReason = (status.EnrollmentError ?? string.Empty).Trim();
        if (hubReason.Length > 0)
            return "The Hub refused this device: " + hubReason;

        // The agent may join a second code after a comma.
        if (AgentLastError.Has(status.LastError, AgentLastError.EnrollmentTokenRefused))
        {
            return "The Hub refused this enrollment token. Make a new one in Devices, Add device.";
        }
        if (AgentLastError.Has(status.LastError, AgentLastError.AgentTokenNotSaved))
        {
            return "The Hub accepted this device, but Windows could not save its agent token. View logs shows why.";
        }

        var state = (status.HubConnectionState ?? string.Empty).Trim();
        if (string.Equals(state, "auth_failed", StringComparison.OrdinalIgnoreCase))
        {
            return "The Hub refused this token. Check that it was copied whole.";
        }

        return null;
    }
}
