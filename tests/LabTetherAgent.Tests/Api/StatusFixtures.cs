using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Api;

/// <summary>
/// The protocol repo's real <c>/agent/status</c> bodies, made by the Go
/// agent's own handler. The copy in Fixtures/localagent is kept in step by
/// scripts/localagent-contract.py. A case no fixture covers is a fixture with
/// one field changed (<see cref="Variant"/>), never hand-written JSON.
/// </summary>
internal static partial class StatusFixtures
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "localagent");

    private static string StatusFolder => Path.Combine(Folder, "fixtures", "status");

    public static IReadOnlyList<string> Names =>
        Directory.GetFiles(StatusFolder, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string Body(string name) => File.ReadAllText(Path.Combine(StatusFolder, name + ".json"));

    public static JsonObject Json(string name) => JsonNode.Parse(Body(name))!.AsObject();

    public static string Variant(string name, Action<JsonObject> edit)
    {
        var body = Json(name);
        edit(body);
        return body.ToJsonString();
    }

    public static JsonObject Codes => JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "codes.v1.json")))!.AsObject();

    /// <summary>What the app holds after one poll: the app's own decoder and mapping.</summary>
    public static AgentStatus Decode(string body) =>
        LocalApiClient.ToAgentStatus(LocalApiClient.DecodeStatusBody(body)
            ?? throw new InvalidOperationException("the decoder returned no status"));

    /// <summary>One real poll through LocalApiClient, returning what it delivered.</summary>
    public static async Task<(AgentStatus Status, bool Connected)> PollAsync(string body)
    {
        using var client = new LocalApiClient(new HttpClient(new BodyHandler(body)));
        AgentStatus? delivered = null;
        client.OnStatusUpdated += status => delivered = status;
        client.Configure("18091", "local-auth-token");
        await client.PollStatusForTestingAsync();
        return (delivered ?? throw new InvalidOperationException("the poll delivered no status"), client.IsConnected);
    }

    /// <summary>A snake_case word such as <c>enrollment_pending</c> is a code, not text.</summary>
    public static void AssertNoCode(string text, string context)
    {
        var match = SnakeCase().Match(text);
        Assert.False(match.Success, $"{context}: '{text}' shows the code '{match.Value}'");
    }

    [GeneratedRegex(@"[a-z]+_[a-z_]+")]
    private static partial Regex SnakeCase();

    private sealed class BodyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
