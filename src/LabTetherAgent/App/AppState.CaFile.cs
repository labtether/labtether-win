using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.App;

public partial class AppState
{
    /// <summary>Asks the Hub about a CA file. Tests replace it.</summary>
    internal Func<string, string, Task<HubCaFileVerdict>>? CheckCaFileOverride { get; set; }

    /// <summary>
    /// App launch. A CA file chosen under Advanced by an earlier version has
    /// not been checked against the Hub. Check it before the agent starts:
    /// when that file alone verifies the Hub, the agent trusts only it from
    /// now on. Otherwise the agent keeps its earlier trust and the check runs
    /// again at the next launch, so a working device stays online.
    /// </summary>
    public async Task StartAgentAfterCaFileCheckAsync()
    {
        try
        {
            await CheckSavedCaFileAsync();
        }
        catch (Exception)
        {
            // Whatever went wrong, the agent starts with the trust it had.
            AgentProcess.LogReader.AppendRaw(HubCaFileCheck.StartNotChecked);
        }

        StartAgent();
    }

    internal async Task CheckSavedCaFileAsync()
    {
        if (!HubCaFileCheck.IsPending(Settings))
            return;
        var hubUrl = Settings.HubUrl;
        var caFile = Settings.TlsCaFile;
        var contents = AgentSettings.CaFileSha256(caFile);
        if (contents.Length == 0)
            return;

        var check = CheckCaFileOverride ?? ((hub, file) => HubCaFileCheck.RunAsync(ConnectionTester, hub, file));
        var verdict = await check(hubUrl, caFile);
        // Setup or Settings may have replaced these while the Hub was asked.
        if (Settings.HubUrl != hubUrl || Settings.TlsCaFile != caFile)
            return;

        switch (verdict)
        {
            case HubCaFileVerdict.Verified:
                Settings.VerifiedCaFileSha256 = contents;
                Settings.Save();
                AgentProcess.LogReader.AppendRaw(HubCaFileCheck.NowOnlyRoot);
                break;
            case HubCaFileVerdict.DoesNotMatch:
                AgentProcess.LogReader.AppendRaw(HubCaFileCheck.StartDoesNotMatch);
                break;
            default:
                AgentProcess.LogReader.AppendRaw(HubCaFileCheck.StartNotChecked);
                break;
        }
    }
}
