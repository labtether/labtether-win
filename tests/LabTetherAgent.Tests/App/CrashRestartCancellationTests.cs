using LabTetherAgent.App;

namespace LabTetherAgent.Tests.App;

public class CrashRestartCancellationTests
{
    [Fact]
    public void CancelCancelsAndClearsCurrentRestart()
    {
        using var cancellation = new CrashRestartCancellation();
        var pending = cancellation.Begin();

        cancellation.Cancel();

        Assert.True(pending.IsCancellationRequested);
        Assert.False(cancellation.IsCurrent(pending));
        pending.Dispose();
    }

    [Fact]
    public void BeginCancelsPreviousRestart()
    {
        using var cancellation = new CrashRestartCancellation();
        var first = cancellation.Begin();

        var second = cancellation.Begin();

        Assert.True(first.IsCancellationRequested);
        Assert.False(cancellation.IsCurrent(first));
        Assert.True(cancellation.IsCurrent(second));
        first.Dispose();
    }

    [Fact]
    public void ClearIfCurrentDoesNotClearNewerRestart()
    {
        using var cancellation = new CrashRestartCancellation();
        var first = cancellation.Begin();
        var second = cancellation.Begin();

        cancellation.ClearIfCurrent(first);

        Assert.True(cancellation.IsCurrent(second));
        first.Dispose();
    }
}
