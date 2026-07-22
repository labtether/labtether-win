using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class AgentSetupTransactionTests
{
    [Fact]
    public async Task RejectedReplacementRollsBackWithoutCommitting()
    {
        var activeIdentity = "prior";
        var commitCalls = 0;
        var rollbackCalls = 0;

        var result = await AgentSetupTransaction.ExecuteAsync(
            _ => Task.FromResult(AgentConnectionAttemptResult.Failed(
                "The enrollment token was rejected.")),
            _ =>
            {
                commitCalls++;
                activeIdentity = "replacement";
                return Task.CompletedTask;
            },
            () =>
            {
                rollbackCalls++;
                activeIdentity = "prior";
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, commitCalls);
        Assert.Equal(1, rollbackCalls);
        Assert.Equal("prior", activeIdentity);
    }

    [Fact]
    public async Task CancellationRollsBackAndNeverCommits()
    {
        using var cancellation = new CancellationTokenSource();
        var attemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var rollbackCalls = 0;
        var commitCalls = 0;

        var transaction = AgentSetupTransaction.ExecuteAsync(
            async token =>
            {
                attemptStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return AgentConnectionAttemptResult.Connected();
            },
            _ =>
            {
                commitCalls++;
                return Task.CompletedTask;
            },
            () =>
            {
                rollbackCalls++;
                return Task.CompletedTask;
            },
            cancellation.Token);

        await attemptStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction);
        Assert.Equal(0, commitCalls);
        Assert.Equal(1, rollbackCalls);
    }

    [Fact]
    public async Task AuthenticatedReplacementCommitsExactlyOnce()
    {
        var activeIdentity = "prior";
        var commitCalls = 0;
        var rollbackCalls = 0;

        var result = await AgentSetupTransaction.ExecuteAsync(
            _ => Task.FromResult(AgentConnectionAttemptResult.Connected()),
            _ =>
            {
                commitCalls++;
                activeIdentity = "replacement";
                return Task.CompletedTask;
            },
            () =>
            {
                rollbackCalls++;
                activeIdentity = "prior";
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, commitCalls);
        Assert.Equal(0, rollbackCalls);
        Assert.Equal("replacement", activeIdentity);
    }

    [Fact]
    public async Task CommitPersistenceFailureRestoresPriorState()
    {
        var activeIdentity = "prior";
        var rollbackCalls = 0;

        var result = await AgentSetupTransaction.ExecuteAsync(
            _ => Task.FromResult(AgentConnectionAttemptResult.Connected()),
            _ =>
            {
                activeIdentity = "partially-written-replacement";
                throw new UnauthorizedAccessException("simulated persistence failure");
            },
            () =>
            {
                rollbackCalls++;
                activeIdentity = "prior";
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("previous setup was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, rollbackCalls);
        Assert.Equal("prior", activeIdentity);
    }
}
