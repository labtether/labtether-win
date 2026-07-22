using LabTetherAgent.Services;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.App;

public class OnboardingConnectionTests
{
    [Fact]
    public void EnrollmentTokenRejectionHasSpecificActionableMessage()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            HubConnectionState = "auth_failed",
            LastError = "enrollment_token_rejected",
        });

        Assert.NotNull(message);
        Assert.Contains("new one-use token", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericAuthenticationFailureDoesNotClaimEnrollmentSucceeded()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            HubConnectionState = "auth_failed",
            LastError = "auth_failed",
        });

        Assert.Equal("The Hub rejected the configured credential. Check the token and try again.", message);
    }

    [Fact]
    public void PendingEnrollmentIsNotMisclassifiedAsFailureOrSuccess()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            HubConnectionState = "connecting",
            LastError = "enrollment_pending",
        });

        Assert.Null(message);
    }

    [Fact]
    public void DurableCredentialPersistenceFailureBlocksSetupCompletion()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            IsConnected = true,
            HubConnectionState = "connected",
            LastError = "agent_token_persistence_failed",
        });

        Assert.NotNull(message);
        Assert.Contains("could not securely save", message, StringComparison.OrdinalIgnoreCase);
    }
}
