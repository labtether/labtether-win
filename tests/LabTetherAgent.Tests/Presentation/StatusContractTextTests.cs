using System.Text.Json.Nodes;
using LabTetherAgent.Presentation;
using LabTetherAgent.Services;
using LabTetherAgent.State;
using LabTetherAgent.Tests.Api;

namespace LabTetherAgent.Tests.Presentation;

/// <summary>
/// What the flyout header, the tray tooltip and Setup show for each real
/// agent state, from the protocol contract's <c>/agent/status</c> bodies
/// decoded by the app's own code. Risks: a real state shows the wrong word,
/// a code shows as text, or the Hub's own words never reach the person.
/// </summary>
public class StatusContractTextTests
{
    private const string Hub = "wss://hub.example.com:8443/ws/agent";
    private const string NotSaved = "Windows could not save the agent token. View logs shows why.";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DroppedAt = new(2026, 11, 14, 9, 38, 51, TimeSpan.Zero);
    private static readonly string Since = "since " + AgentStatusText.FormatClock(DroppedAt, Now);
    private static readonly string OfflineSince = "Offline " + Since;

    /// <param name="Setup">What Setup reports as the end of enrollment, or null while it waits.</param>
    private sealed record Shown(
        string Body, string Word, string Detail, bool Problem, TrayGlyphState Glyph, string Tray, string? Setup = null);

    private static TheoryData<string> Labels(IEnumerable<string> labels)
    {
        var data = new TheoryData<string>();
        foreach (var label in labels) data.Add(label);
        return data;
    }

    private static void AssertShown(Shown expected, string body)
    {
        var status = StatusFixtures.Decode(body);
        var header = AgentStatusText.Header(status, Hub, Now);
        var tray = TrayStatus.Resolve(status.IsConnected, status, null, null, Now);
        var setup = AgentSetupStatusClassifier.TerminalFailureMessage(status);

        Assert.Equal((expected.Body, expected.Word, expected.Detail, expected.Problem),
            (expected.Body, header.StatusWord, header.Detail, header.IsProblem));
        Assert.Equal((expected.Body, expected.Glyph, TrayStatus.AppName + "\n" + expected.Tray),
            (expected.Body, tray.State, tray.Tooltip));
        Assert.Equal((expected.Body, expected.Setup), (expected.Body, setup));
        foreach (var text in new[] { header.StatusWord, header.Detail, tray.Tooltip, setup ?? "" })
            StatusFixtures.AssertNoCode(text, expected.Body);
    }

    private const string Host = "hub.example.com";

    // Faults: after setup, a device an admin rejected read "Offline", and an
    // expired enrollment token read the app's fixed line instead of the
    // Hub's words; the tray said "Offline" for both. A connected agent whose
    // token was not saved showed a plain "Connected" in the tray, though it
    // loses the Hub at the next start.
    private static readonly Shown[] States =
    [
        new("connected", "Connected", Host + " · up 2 h 14 min", false, TrayGlyphState.Ok, "Connected"),
        new("alerts-with-temperature", "Connected", Host + " · up 2 h 14 min", false,
            TrayGlyphState.Warning, "Disk usage above 90%"),
        new("connected-token-persistence-failed", "Connected", Host + " · " + NotSaved, true,
            TrayGlyphState.Warning, "Connected · agent token not saved",
            "The Hub accepted this device, but Windows could not save its agent token. View logs shows why."),
        new("connecting-transient-host-unreachable", "Offline", "Can't reach the Hub · " + Since, true,
            TrayGlyphState.Offline, OfflineSince),
        new("enrollment-pending", "Waiting for approval", "Approve this device in Devices on the Hub", false,
            TrayGlyphState.Offline, "Waiting for approval"),
        new("auth-failed-token-rejected", "Offline",
            "The Hub refused the agent token. Choose Re-enroll in the tray menu. · " + Since, true,
            TrayGlyphState.Offline, OfflineSince, "The Hub refused this token. Check that it was copied whole."),
        new("auth-failed-enrollment-token-rejected", "Enrollment refused",
            "This enrollment token expired. Make a new one in Devices, Add device.", true,
            TrayGlyphState.Offline, "Enrollment refused",
            "The Hub refused this device: This enrollment token expired. Make a new one in Devices, Add device."),
        new("enrollment-refused-hub-words", "Enrollment refused", "An admin rejected this device in LabTether.", true,
            TrayGlyphState.Offline, "Enrollment refused",
            "The Hub refused this device: An admin rejected this device in LabTether."),
        new("disconnected-no-transport", "Offline", Host, true, TrayGlyphState.Offline, "Offline"),
        new("minimal-optional-fields-absent", "Offline", Host, true, TrayGlyphState.Offline, "Offline"),
    ];

    public static TheoryData<string> StateNames => Labels(States.Select(item => item.Body));

    [Theory]
    [MemberData(nameof(StateNames))]
    public void EachContractStateShowsItsWords(string fixture) =>
        AssertShown(States.Single(item => item.Body == fixture), StatusFixtures.Body(fixture));

    [Fact]
    public void EveryFixtureHasAScreenCheck() =>
        Assert.Equal(StatusFixtures.Names, States.Select(item => item.Body).Order(StringComparer.Ordinal));

    // Code pairs the contract allows that no fixture holds: a fixture with one
    // field changed. Fault: a last_error with two codes joined by a comma, or
    // a refused enrollment token without the Hub's words, read wrong.
    private const string Both = "agent_token_persistence_failed,consumed_enrollment_token_cleanup_failed";
    private const string SavedTokenSetup =
        "The Hub accepted this device, but Windows could not save its agent token. View logs shows why.";

    private static readonly (Shown Shown, string Fixture, Action<JsonObject> Edit)[] Pairs =
    [
        (new("transient, no reason", "Offline", Host + " · " + Since, true, TrayGlyphState.Offline, OfflineSince),
            "connecting-transient-host-unreachable", body => body.Remove("last_error_reason")),
        (new("connecting, token not saved", "Offline", NotSaved, true, TrayGlyphState.Offline, "Offline", SavedTokenSetup),
            "enrollment-pending", body => body["last_error"] = "agent_token_persistence_failed"),
        (new("connecting, two codes", "Offline", NotSaved, true, TrayGlyphState.Offline, "Offline", SavedTokenSetup),
            "enrollment-pending", body => body["last_error"] = Both),
        (new("connected, two codes", "Connected", Host + " · " + NotSaved, true,
            TrayGlyphState.Warning, "Connected · agent token not saved", SavedTokenSetup),
            "connected", body => body["last_error"] = Both),
        (new("connected, two codes reversed", "Connected", Host + " · " + NotSaved, true,
            TrayGlyphState.Warning, "Connected · agent token not saved", SavedTokenSetup),
            "connected", body => body["last_error"] = "consumed_enrollment_token_cleanup_failed,agent_token_persistence_failed"),
        (new("connected, used token kept", "Connected", Host + " · up 2 h 14 min", false, TrayGlyphState.Ok, "Connected"),
            "connected", body => body["last_error"] = "consumed_enrollment_token_cleanup_failed"),
        // An agent before 2.0 sends no Hub words.
        (new("enrollment token refused, no Hub words", "Enrollment refused",
            "The Hub refused this enrollment token. Make a new one in Devices, Add device.", true,
            TrayGlyphState.Offline, "Enrollment refused",
            "The Hub refused this enrollment token. Make a new one in Devices, Add device."),
            "auth-failed-enrollment-token-rejected", body => body.Remove("enrollment_error")),
    ];

    public static TheoryData<string> PairNames => Labels(Pairs.Select(item => item.Shown.Body));

    [Theory]
    [MemberData(nameof(PairNames))]
    public void CodePairsWithoutAFixtureReadAsWords(string label)
    {
        var (shown, fixture, edit) = Pairs.Single(item => item.Shown.Body == label);
        AssertShown(shown, StatusFixtures.Variant(fixture, edit));
    }

    // Fault: a code from a newer agent shown as it is, or a crash on it. The
    // app keeps its generic line instead.
    private static readonly (Shown Shown, string Fixture, Action<JsonObject> Edit)[] Unknown =
    [
        (new("unknown last_error, connecting", "Offline", Host + " · " + Since, true, TrayGlyphState.Offline, OfflineSince),
            "connecting-transient-host-unreachable", body =>
            {
                body["last_error"] = "hub_moved_elsewhere";
                body.Remove("last_error_reason");
            }),
        (new("unknown last_error_reason", "Offline", Host + " · " + Since, true, TrayGlyphState.Offline, OfflineSince),
            "connecting-transient-host-unreachable", body => body["last_error_reason"] = "dns_poisoned"),
        (new("unknown last_error, auth_failed", "Offline", "The Hub refused the agent token · " + Since, true,
            TrayGlyphState.Offline, OfflineSince, "The Hub refused this token. Check that it was copied whole."),
            "auth-failed-token-rejected", body =>
            {
                body["last_error"] = "hub_key_rotated";
                body.Remove("last_error_reason");
            }),
        (new("unknown last_error, connected", "Connected", Host + " · up 2 h 14 min", false, TrayGlyphState.Ok, "Connected"),
            "connected", body => body["last_error"] = "hub_moved_elsewhere"),
        (new("unknown connection_state", "Offline", Host, true, TrayGlyphState.Offline, "Offline"),
            "disconnected-no-transport", body => body["connection_state"] = "paused"),
    ];

    public static TheoryData<string> UnknownNames => Labels(Unknown.Select(item => item.Shown.Body));

    [Theory]
    [MemberData(nameof(UnknownNames))]
    public void UnknownCodesShowTheGenericLine(string label)
    {
        var (shown, fixture, edit) = Unknown.Single(item => item.Shown.Body == label);
        AssertShown(shown, StatusFixtures.Variant(fixture, edit));
    }

    // Fault: a firing alert from a real body lost its title, summary or start
    // time on the way to the alert list; a resolved alert was listed.
    [Fact]
    public void FiringAlertsListTitleSummaryAndStart()
    {
        var status = StatusFixtures.Decode(StatusFixtures.Body("alerts-with-temperature"));
        var started = new DateTimeOffset(2026, 11, 14, 9, 12, 40, TimeSpan.Zero);

        var rows = AgentStatusText.AlertRows(status, Now);

        var row = Assert.Single(rows);
        Assert.Equal(new AlertRow("Disk usage above 90%",
            "/srv/media is 93% full · since " + AgentStatusText.FormatClock(started, Now), false), row);
    }
}
