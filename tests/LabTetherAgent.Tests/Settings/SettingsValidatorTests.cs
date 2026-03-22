using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class SettingsValidatorTests
{
    [Theory]
    [InlineData("https://hub.example.com", true)]
    [InlineData("wss://hub.example.com/ws/agent", true)]
    [InlineData("http://192.168.1.100:8080", true)]
    [InlineData("ws://localhost:8443", true)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("ftp://hub.example.com", false)]
    [InlineData("   ", false)]
    public void IsValidHubUrl(string? url, bool expected)
    {
        Assert.Equal(expected, SettingsValidator.IsValidHubUrl(url));
    }

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void IsValidToken(string? token, bool expected)
    {
        Assert.Equal(expected, SettingsValidator.IsValidToken(token));
    }

    [Theory]
    [InlineData("8080", true)]
    [InlineData("1", true)]
    [InlineData("65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void IsValidPort(string? port, bool expected)
    {
        Assert.Equal(expected, SettingsValidator.IsValidPort(port));
    }

    [Theory]
    [InlineData("debug", true)]
    [InlineData("info", true)]
    [InlineData("warn", true)]
    [InlineData("error", true)]
    [InlineData("INFO", true)]
    [InlineData("trace", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidLogLevel(string? level, bool expected)
    {
        Assert.Equal(expected, SettingsValidator.IsValidLogLevel(level));
    }

    [Theory]
    [InlineData("https://hub.example.com", "wss://hub.example.com/ws/agent")]
    [InlineData("https://hub.example.com/", "wss://hub.example.com/ws/agent")]
    [InlineData("wss://hub.example.com/ws/agent", "wss://hub.example.com/ws/agent")]
    [InlineData("http://192.168.1.100:8080", "ws://192.168.1.100:8080/ws/agent")]
    [InlineData("not-a-url", null)]
    [InlineData("", null)]
    public void NormalizeHubWebSocketUrl(string? input, string? expected)
    {
        Assert.Equal(expected, SettingsValidator.NormalizeHubWebSocketUrl(input));
    }

    [Theory]
    [InlineData("wss://hub.example.com/ws/agent", "https://hub.example.com")]
    [InlineData("ws://192.168.1.100:8080/ws/agent", "http://192.168.1.100:8080")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DeriveApiBaseUrl(string? wsUrl, string? expected)
    {
        Assert.Equal(expected, SettingsValidator.DeriveApiBaseUrl(wsUrl));
    }
}
