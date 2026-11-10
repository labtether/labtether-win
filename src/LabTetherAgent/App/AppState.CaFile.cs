using LabTetherAgent.Services;

namespace LabTetherAgent.App;

// A CA file chosen under Advanced by a version before the CA-only rule was
// never checked against the Hub. Until it is, the agent keeps that version's
// trust (the file and Windows' own list), so an update cannot take a working
// device offline. The app asks the Hub at every launch and after every
// reconnect. The first time the file alone verifies the Hub its fingerprint
// is recorded, and from then on the agent trusts only that CA: nothing here
// ever removes the record.
public partial class AppState
{
    private int _caFileCheckRunning;

    /// <summary>Asks the Hub about a CA file. Tests replace it.</summary>
    internal Func<string, string, Task<HubCaFileVerdict>>? CheckCaFileOverride { get; set; }

    /// <summary>
    /// Set while the saved CA file is known not to sign this Hub's
    /// certificate. Settings shows it beside the field.
    /// </summary>
    public string? CaFileNotice { get; private set; }

    /// <summary>App launch: the check runs before the agent starts.</summary>
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

    /// <summary>
    /// After a reconnect, and when Settings opens: ask again while the file
    /// is unchecked. A running agent is restarted once the file is verified,
    /// so it trusts only that CA at once.
    /// </summary>
    public async Task RecheckCaFileAsync()
    {
        try
        {
            if (await CheckSavedCaFileAsync() == HubCaFileVerdict.Verified && AgentProcess.IsRunning)
                await RestartAgentAsync();
        }
        catch (Exception)
        {
            // The next reconnect or launch asks again.
        }
    }

    internal void OnHubConnectionChanged(bool connected)
    {
        // A mismatch already shown stays shown until Settings or a launch asks again.
        if (connected && !_disposed && CaFileNotice == null)
            _ = RecheckCaFileAsync();
    }

    internal async Task<HubCaFileVerdict?> CheckSavedCaFileAsync()
    {
        if (!HubCaFileCheck.IsPending(Settings))
        {
            CaFileNotice = null;
            return null;
        }
        if (Interlocked.Exchange(ref _caFileCheckRunning, 1) == 1)
            return null;
        try
        {
            var hubUrl = Settings.HubUrl;
            var caFile = Settings.TlsCaFile;
            var fingerprint = HubCaFileCheck.Fingerprint(caFile);
            var check = CheckCaFileOverride ?? ((hub, file) => HubCaFileCheck.RunAsync(ConnectionTester, hub, file));
            // An unreadable file cannot be checked; the next start or reconnect tries again.
            var verdict = fingerprint.Length == 0 ? HubCaFileVerdict.NotChecked : await check(hubUrl, caFile);
            // Setup or Settings may have replaced these while the Hub was asked.
            if (Settings.HubUrl != hubUrl || Settings.TlsCaFile != caFile || !HubCaFileCheck.IsPending(Settings))
                return null;

            switch (verdict)
            {
                case HubCaFileVerdict.Verified:
                    Settings.EnrolledHubCaFingerprint = fingerprint;
                    Settings.Save();
                    CaFileNotice = null;
                    AgentProcess.LogReader.AppendRaw(HubCaFileCheck.NowOnlyRoot);
                    break;
                case HubCaFileVerdict.DoesNotMatch:
                    if (CaFileNotice == null)
                        AgentProcess.LogReader.AppendRaw(HubCaFileCheck.StartDoesNotMatch);
                    CaFileNotice = HubCaFileCheck.DoesNotMatch;
                    break;
                default:
                    AgentProcess.LogReader.AppendRaw(HubCaFileCheck.StartNotChecked);
                    break;
            }

            return verdict;
        }
        finally
        {
            Volatile.Write(ref _caFileCheckRunning, 0);
        }
    }
}
