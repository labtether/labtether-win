using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

// Risk: Save in Settings, setup and re-enroll restart the agent for a few
// seconds. Each restart showed "Offline since" and "Reconnected", and the
// five-minute throttle then hid a real outage right after.
public class ConnectionToastGateTests
{
    private readonly List<string> _shown = [];
    private TaskCompletionSource _grace = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ConnectionToastGate Gate() => new(
        () => _shown.Add("offline"), () => _shown.Add("reconnected"),
        async (_, token) =>
        {
            using (token.Register(() => _grace.TrySetCanceled(token)))
                await _grace.Task;
        });

    [Fact]
    public async Task ShortRestartShowsNothing()
    {
        var gate = Gate();
        gate.OnConnectionChanged(true);
        gate.OnConnectionChanged(false);
        gate.OnConnectionChanged(true);
        _grace.TrySetResult();
        await Task.Delay(50);

        Assert.Empty(_shown);
    }

    [Fact]
    public async Task OutageLongerThanGraceShowsOfflineThenReconnected()
    {
        var gate = Gate();
        gate.OnConnectionChanged(true);
        gate.OnConnectionChanged(false);
        _grace.TrySetResult();
        await Task.Delay(50);
        gate.OnConnectionChanged(true);

        Assert.Equal(new[] { "offline", "reconnected" }, _shown);
    }

    [Fact]
    public async Task FirstReportAfterStartShowsNothing()
    {
        var gate = Gate();
        gate.OnConnectionChanged(false);
        _grace.TrySetResult();
        await Task.Delay(50);

        Assert.Empty(_shown);
    }
}
