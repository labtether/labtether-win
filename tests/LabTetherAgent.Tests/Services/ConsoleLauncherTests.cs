using System.Net;
using System.Text;
using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class ConsoleLauncherTests
{
    // Add device hands out the Hub API port as the Hub URL. Its "/" is JSON, so
    // Open Console must follow console_url instead of opening that JSON.
    [Fact]
    public async Task ResolveAsyncOpensTheConsoleNamedByTheHubApiRoot()
    {
        Uri? requested = null;
        var launcher = new ConsoleLauncher((_, _) => new StubHandler(request =>
        {
            requested = request.RequestUri;
            return Json("""{"service":"labtether-hub","console_url":"http://hub.example.test:3000"}""");
        }));

        var url = await launcher.ResolveAsync("wss://hub.example.test:8443/ws/agent", false, null);

        Assert.Equal("http://hub.example.test:3000/", url);
        Assert.Equal(new Uri("https://hub.example.test:8443/"), requested);
    }

    // A Hub URL that already is the console (it redirects to sign-in or serves
    // a page) and an unreachable Hub both keep the old behaviour.
    [Theory]
    [InlineData("redirect")]
    [InlineData("html")]
    [InlineData("offline")]
    public async Task ResolveAsyncKeepsTheHubUrlWhenTheHubNamesNoConsole(string answer)
    {
        var launcher = new ConsoleLauncher((_, _) => new StubHandler(request => answer switch
        {
            "redirect" => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("/en/login", UriKind.Relative) },
            },
            "html" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<!doctype html><title>LabTether</title>", Encoding.UTF8, "text/html"),
            },
            _ => throw new HttpRequestException("connection refused"),
        }));

        var url = await launcher.ResolveAsync("https://console.example.test", false, null);

        Assert.Equal("https://console.example.test", url);
    }

    // The answer goes to the shell, so a Hub reply must never make the tray
    // start a local program or send stored user information.
    [Theory]
    [InlineData("""{"service":"labtether-hub","console_url":"file:///C:/Windows/System32/calc.exe"}""")]
    [InlineData("""{"service":"labtether-hub","console_url":"javascript:alert(1)"}""")]
    [InlineData("""{"service":"labtether-hub","console_url":"http://user:secret@hub.example.test:3000"}""")]
    [InlineData("""{"service":"other","console_url":"http://hub.example.test:3000"}""")]
    public void ConsoleUrlFromHubRootRefusesUnsafeOrForeignAnswers(string body)
    {
        Assert.Null(ConsoleLauncher.ConsoleUrlFromHubRoot(Encoding.UTF8.GetBytes(body)));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
