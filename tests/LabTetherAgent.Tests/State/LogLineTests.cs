using LabTetherAgent.State;

namespace LabTetherAgent.Tests.State;

public class LogLineTests
{
    [Fact]
    public void Parse_GoLogFormat()
    {
        var line = LogLine.Parse("2026/03/21 14:30:45 Agent started successfully");

        Assert.Equal(2026, line.Timestamp.Year);
        Assert.Equal(3, line.Timestamp.Month);
        Assert.Equal(21, line.Timestamp.Day);
        Assert.Equal(14, line.Timestamp.Hour);
        Assert.Equal(30, line.Timestamp.Minute);
        Assert.Equal("info", line.Level);
        Assert.Equal("Agent started successfully", line.Message);
    }

    [Fact]
    public void Parse_ErrorLevel()
    {
        var line = LogLine.Parse("2026/03/21 14:30:45 ERROR: connection failed");

        Assert.Equal("error", line.Level);
        Assert.Equal("ERROR: connection failed", line.Message);
    }

    [Fact]
    public void Parse_WarningLevel()
    {
        var line = LogLine.Parse("2026/03/21 14:30:45 WARNING: disk usage high");

        Assert.Equal("warning", line.Level);
    }

    [Fact]
    public void Parse_BracketedLevel()
    {
        var line = LogLine.Parse("2026/03/21 14:30:45 [ERROR] something broke");

        Assert.Equal("error", line.Level);
    }

    [Fact]
    public void Parse_NonGoFormat_FallsBack()
    {
        var line = LogLine.Parse("Some random log output without timestamp");

        Assert.Equal("info", line.Level);
        Assert.Equal("Some random log output without timestamp", line.Message);
        Assert.Equal("Some random log output without timestamp", line.Raw);
    }

    [Fact]
    public void Parse_EmptyString()
    {
        var line = LogLine.Parse("");

        Assert.Equal("info", line.Level);
        Assert.Equal(string.Empty, line.Message);
    }

    [Fact]
    public void Parse_Null()
    {
        var line = LogLine.Parse(null!);

        Assert.Equal("info", line.Level);
    }

    [Fact]
    public void Parse_DebugLevel()
    {
        var line = LogLine.Parse("2026/03/21 14:30:45 [DEBUG] trace info here");

        Assert.Equal("debug", line.Level);
    }

    [Fact]
    public void Parse_PreservesRaw()
    {
        var raw = "2026/03/21 14:30:45 some message";
        var line = LogLine.Parse(raw);

        Assert.Equal(raw, line.Raw);
    }
}
