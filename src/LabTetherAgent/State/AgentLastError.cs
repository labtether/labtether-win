namespace LabTetherAgent.State;

/// <summary>
/// Plain words for the agent's fixed codes in /agent/status. People never
/// see a code as it is; an unknown or empty code gives null, so the caller
/// keeps its generic text. The meaning matches the Mac app
/// (Presentation/AgentLastError.swift and HubConnectReason.swift).
/// </summary>
public static class AgentLastError
{
    /// <summary>
    /// <c>last_error</c> values (labtether-agent internal/agentcore):
    /// <c>transient</c> and <c>auth_failed</c> from connectErrorKind
    /// (ws_transport.go, ws_reconnect.go); <c>enrollment_pending</c>,
    /// <c>enrollment_token_rejected</c> and
    /// <c>agent_token_persistence_failed</c> from ws_transport.go and run.go;
    /// <c>consumed_enrollment_token_cleanup_failed</c> from run.go, which may
    /// follow another code after a comma.
    /// </summary>
    public const string Transient = "transient";
    public const string AgentTokenRefused = "auth_failed";
    public const string WaitingForApproval = "enrollment_pending";
    public const string EnrollmentTokenRefused = "enrollment_token_rejected";
    public const string AgentTokenNotSaved = "agent_token_persistence_failed";
    public const string UsedEnrollmentTokenKept = "consumed_enrollment_token_cleanup_failed";

    public const string AgentTokenRefusedText = "The Hub refused the agent token";
    public const string WaitingForApprovalText = "Approve this device in Devices on the Hub";

    /// <summary>The known codes in a <c>last_error</c> value, in order.</summary>
    public static IReadOnlyList<string> Codes(string? lastError) =>
        (lastError ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.ToLowerInvariant())
            .ToList();

    public static bool Has(string? lastError, string code) => Codes(lastError).Contains(code);

    /// <summary>Words for <c>last_error</c>, or null when there is nothing to tell the person.</summary>
    public static string? Words(string? lastError) =>
        Codes(lastError).Select(Single).FirstOrDefault(text => text != null);

    private static string? Single(string code) => code switch
    {
        AgentTokenRefused => AgentTokenRefusedText + ". Choose Re-enroll in the tray menu.",
        WaitingForApproval => WaitingForApprovalText,
        EnrollmentTokenRefused => "The Hub refused this enrollment token. Make a new one in Devices, Add device.",
        AgentTokenNotSaved => "Windows could not save the agent token. View logs shows why.",
        // transient: last_error_reason says why. The used enrollment token is
        // recorded by the agent and never sent again.
        _ => null,
    };

    /// <summary>
    /// Words for <c>last_error_reason</c> (ws_connect_reason.go), the cause of
    /// the last failed Hub connect, or null for an empty or unknown code.
    /// </summary>
    public static string? ReasonWords(string? reason) =>
        (reason ?? string.Empty).Trim() switch
        {
            "name_lookup_failed" => "Can't find the Hub's host name",
            "connection_refused" => "The Hub refused the connection",
            "host_unreachable" => "Can't reach the Hub",
            "timed_out" => "The Hub did not answer in time",
            "certificate_untrusted" => "The Hub's certificate is not trusted",
            "token_rejected" => AgentTokenRefusedText,
            _ => null,
        };
}
