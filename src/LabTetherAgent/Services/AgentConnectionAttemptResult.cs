namespace LabTetherAgent.Services;

public sealed record AgentConnectionAttemptResult(bool Success, string Message)
{
    public static AgentConnectionAttemptResult Connected() =>
        new(true, "Connected to the Hub.");

    public static AgentConnectionAttemptResult Failed(string message) =>
        new(false, message);
}
