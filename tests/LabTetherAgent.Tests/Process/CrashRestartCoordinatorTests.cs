using LabTetherAgent.Process;

namespace LabTetherAgent.Tests.Process;

public class CrashRestartCoordinatorTests
{
    [Fact]
    public void FirstCrash_ReturnsBaseDelay()
    {
        var coord = new CrashRestartCoordinator();
        var delay = coord.NextDelay();
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void ExponentialBackoff_DoublesEachTime()
    {
        var coord = new CrashRestartCoordinator();
        var first = coord.NextDelay();  // 1s
        var second = coord.NextDelay(); // 2s
        var third = coord.NextDelay();  // 4s
        var fourth = coord.NextDelay(); // 8s

        Assert.Equal(TimeSpan.FromSeconds(1), first);
        Assert.Equal(TimeSpan.FromSeconds(2), second);
        Assert.Equal(TimeSpan.FromSeconds(4), third);
        Assert.Equal(TimeSpan.FromSeconds(8), fourth);
    }

    [Fact]
    public void MaxDelay_CapsAt60Seconds()
    {
        var coord = new CrashRestartCoordinator();
        TimeSpan delay = TimeSpan.Zero;
        for (int i = 0; i < 10; i++)
            delay = coord.NextDelay();

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Fact]
    public void Reset_RestartsBackoff()
    {
        var coord = new CrashRestartCoordinator();
        coord.NextDelay(); // 1s
        coord.NextDelay(); // 2s
        coord.NextDelay(); // 4s
        coord.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), coord.NextDelay());
        Assert.Equal(0, coord.AttemptCount - 1); // back to attempt 1
    }

    [Fact]
    public void AttemptCount_TracksCorrectly()
    {
        var coord = new CrashRestartCoordinator();
        Assert.Equal(0, coord.AttemptCount);

        coord.NextDelay();
        Assert.Equal(1, coord.AttemptCount);

        coord.NextDelay();
        Assert.Equal(2, coord.AttemptCount);

        coord.Reset();
        Assert.Equal(0, coord.AttemptCount);
    }
}
