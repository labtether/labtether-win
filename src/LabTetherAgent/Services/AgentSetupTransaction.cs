namespace LabTetherAgent.Services;

/// <summary>
/// Coordinates the externally visible setup transaction. The attempt works
/// only with staged state; commit is called only after it proves authenticated
/// connectivity, and every unsuccessful exit restores the prior installation.
/// </summary>
internal static class AgentSetupTransaction
{
    private const string PersistenceFailureMessage =
        "LabTether could not save the new setup. The previous setup is back in place. Check the permissions of your LabTether folder, then try again.";

    public static async Task<AgentConnectionAttemptResult> ExecuteAsync(
        Func<CancellationToken, Task<AgentConnectionAttemptResult>> attempt,
        Func<CancellationToken, Task> commit,
        Func<Task> rollback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(rollback);

        try
        {
            var result = await attempt(cancellationToken);
            if (!result.Success)
            {
                return await RollBackResultAsync(result, rollback);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await commit(cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(rollback);
            throw;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            var rollbackSucceeded = await TryRollbackAsync(rollback);
            return AgentConnectionAttemptResult.Failed(
                rollbackSucceeded
                    ? PersistenceFailureMessage
                    : "LabTether could not save the new setup or fully put back the previous one. Quit LabTether and check the permissions of your LabTether folder before you try again.");
        }
    }

    private static async Task<AgentConnectionAttemptResult> RollBackResultAsync(
        AgentConnectionAttemptResult result,
        Func<Task> rollback)
    {
        if (await TryRollbackAsync(rollback))
            return result;

        return AgentConnectionAttemptResult.Failed(
            $"{result.Message} The previous setup could not be fully put back. Quit LabTether and check the permissions of your LabTether folder before you try again.");
    }

    private static async Task<bool> TryRollbackAsync(Func<Task> rollback)
    {
        try
        {
            await rollback();
            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    internal static bool IsRecoverable(Exception ex) =>
        ex is IOException or
        UnauthorizedAccessException or
        ArgumentException or
        InvalidOperationException or
        PlatformNotSupportedException or
        System.ComponentModel.Win32Exception or
        System.Net.Sockets.SocketException or
        System.Runtime.InteropServices.COMException or
        System.Security.Cryptography.CryptographicException or
        System.Security.SecurityException;
}
