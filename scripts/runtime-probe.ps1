# Runs a short probe executable and requires exit code 0 within the timeout.
#
# Start-Process -PassThru can return a Process that holds no handle to the
# child. If the child exits before anything opens one, Windows PowerShell can no
# longer read the exit status and ExitCode is $null. Over SSH on the Windows
# release host (2026-10-10) that made a passing WinUI probe look like a failure,
# while a direct System.Diagnostics.Process start returned the real exit 0.
# Process.Start keeps the handle from process creation, so the exit code stays
# readable after the child exits. A missing exit code still fails closed.
function Invoke-RuntimeProbe {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Description,
        [string]$Arguments = "--winui-runtime-probe",
        [int]$TimeoutMilliseconds = 30000
    )
    $StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = $FilePath
    $StartInfo.Arguments = $Arguments
    $StartInfo.WorkingDirectory = $WorkingDirectory
    $StartInfo.UseShellExecute = $false
    $Process = [Diagnostics.Process]::new()
    $Process.StartInfo = $StartInfo
    try {
        if (-not $Process.Start()) {
            throw "$Description could not be started"
        }
        if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
            throw "$Description timed out after $([int]($TimeoutMilliseconds / 1000)) seconds"
        }
        # The parameterless wait also completes the process exit bookkeeping.
        $Process.WaitForExit()
        $ExitCode = $null
        try { $ExitCode = $Process.ExitCode } catch { $ExitCode = $null }
        if ($null -eq $ExitCode) {
            throw "$Description did not report an exit code; treating it as failed"
        }
        if ($ExitCode -ne 0) {
            throw "$Description failed with exit code $ExitCode"
        }
    }
    finally {
        $Process.Dispose()
    }
}
