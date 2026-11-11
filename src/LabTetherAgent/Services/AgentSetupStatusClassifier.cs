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

        var lastError = (status.LastError ?? string.Empty).Trim().ToLowerInvariant();
        if (lastError == "enrollment_token_rejected")
        {
            return "The Hub refused this enrollment token. Make a new one in Devices, Add device.";
        }
        if (lastError == "agent_token_persistence_failed")
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
