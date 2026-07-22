using LabTetherAgent.State;

namespace LabTetherAgent.Services;

internal static class AgentSetupStatusClassifier
{
    public static string? TerminalFailureMessage(AgentStatus status)
    {
        var lastError = (status.LastError ?? string.Empty).Trim().ToLowerInvariant();
        if (lastError == "enrollment_token_rejected")
        {
            return "The enrollment token was rejected. Generate a new one-use token in the Hub and try again.";
        }
        if (lastError == "agent_token_persistence_failed")
        {
            return "Enrollment succeeded, but Windows could not securely save the durable agent credential. Check the agent logs and permissions, then try again.";
        }

        var state = (status.HubConnectionState ?? string.Empty).Trim();
        if (string.Equals(state, "auth_failed", StringComparison.OrdinalIgnoreCase))
        {
            return "The Hub rejected the configured credential. Check the token and try again.";
        }

        return null;
    }
}
