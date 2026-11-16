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
        Assert.Contains("Make a new one", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericAuthenticationFailureDoesNotClaimEnrollmentSucceeded()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            HubConnectionState = "auth_failed",
            LastError = "auth_failed",
        });

        Assert.Equal("The Hub refused this token. Check that it was copied whole.", message);
    }

    // Re-enrolling a device ID the Hub already has failed with fixed text that
    // did not say why; the agent now reports the Hub's reason.
    [Fact]
    public void HubReasonForARefusedEnrollmentIsShown()
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            HubConnectionState = "connecting",
            LastError = "enrollment_pending",
            EnrollmentError = "device ltqa-a is already enrolled",
        });

        Assert.Equal("The Hub refused this device: device ltqa-a is already enrolled", message);
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

    // The agent joins a second warning after a comma (run.go); setup passed
    // as if the token had been saved.
    [Theory]
    [InlineData("agent_token_persistence_failed")]
    [InlineData("agent_token_persistence_failed,consumed_enrollment_token_cleanup_failed")]
    public void DurableCredentialPersistenceFailureBlocksSetupCompletion(string lastError)
    {
        var message = AgentSetupStatusClassifier.TerminalFailureMessage(new AgentStatus
        {
            IsConnected = true,
            HubConnectionState = "connected",
            LastError = lastError,
        });

        Assert.NotNull(message);
        Assert.Contains("could not save its agent token", message, StringComparison.OrdinalIgnoreCase);
    }
}
