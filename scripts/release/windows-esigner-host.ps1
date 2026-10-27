# Local OS operations for sign_windows_esigner.py. Input arrives on stdin so
# selectors stay out of launcher arguments and console logs. SignTool itself
# requires the public certificate thumbprint in its /sha1 argument.
Set-PSDebug -Off
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-LocalSigningHost {
    if ($env:OS -ne "Windows_NT" -or $env:GITHUB_ACTIONS -eq "true" -or $env:CI -eq "true") {
        throw "Signing needs an interactive local Windows host"
    }
}

function Get-SelectedCertificate([string]$Thumbprint) {
    if ($Thumbprint -cnotmatch '\A[0-9A-F]{40}\z') { throw "Invalid certificate selector" }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt [DateTime]::Now -or
        $certificate.NotAfter -le [DateTime]::Now -or
        @($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq "1.3.6.1.5.5.7.3.3" }).Count -ne 1) {
        throw "A valid code-signing certificate with a configured private-key provider is required"
    }
    return $certificate
}

function Assert-SelectedSignature([string]$Path, [string]$Thumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or $null -eq $signature.TimeStamperCertificate -or
        $signature.SignerCertificate.Thumbprint -cne $Thumbprint) {
        throw "A valid timestamped signature from the selected certificate is required"
    }
}

function Resolve-SignTool {
    $sdk = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tools = @(Get-ChildItem -LiteralPath $sdk -Directory |
        Where-Object { $_.Name -match '\A10\.0\.[0-9]+\.[0-9]+\z' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($tools.Count -eq 0) { throw "Windows SDK SignTool is unavailable" }
    $tool = $tools[0]
    $signature = Get-AuthenticodeSignature -LiteralPath $tool
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw "Windows SDK SignTool must have a valid Microsoft signature"
    }
    return $tool
}

function Invoke-OneSignature([string]$Path, [string]$Thumbprint, [string]$Tag, [string]$Confirmation, [int]$Budget) {
    if ($Tag -cnotmatch '\Av[0-9]+\.[0-9]+\.[0-9]+\z' -or $Tag -cne $Confirmation -or $Budget -ne 4) {
        throw "Signing needs exact tag and four-signature paid approval"
    }
    $null = Get-SelectedCertificate $Thumbprint
    if ((Get-AuthenticodeSignature -LiteralPath $Path).Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
        throw "The paid signer only accepts an unsigned input"
    }
    if ($Path.Contains('"')) { throw "Invalid signing path" }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = Resolve-SignTool
    $process.StartInfo.Arguments = "sign /fd SHA256 /tr http://ts.ssl.com /td SHA256 /s My /sha1 $Thumbprint `"$Path`""
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    try {
        if (-not $process.Start()) { throw "SignTool could not start" }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "SignTool timed out; the reserved signature cannot be retried"
        }
        $null = $output.GetAwaiter().GetResult()
        $null = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "SignTool failed; the reserved signature cannot be retried" }
    }
    finally { $process.Dispose() }
    Assert-SelectedSignature $Path $Thumbprint
}

try {
    Assert-LocalSigningHost
    $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
    switch ($request.action) {
        "source" {
            . (Join-Path $PSScriptRoot "windows-release-policy.ps1")
            Assert-TrackedSourcePolicy $request.path
        }
        "protect" {
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            & icacls.exe $request.path /inheritance:r /grant:r "*$sid`:(OI)(CI)F" *> $null
            if ($LASTEXITCODE -ne 0) { throw "Private staging permissions could not be set" }
        }
        "certificate" {
            $null = Get-SelectedCertificate $request.thumbprint
            $null = Resolve-SignTool
        }
        "unsigned" {
            if ((Get-AuthenticodeSignature -LiteralPath $request.path).Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
                throw "An unsigned input is required"
            }
        }
        "verify" { Assert-SelectedSignature $request.path $request.thumbprint }
        "sign" {
            Invoke-OneSignature $request.path $request.thumbprint $request.tag $request.confirm_sign $request.confirm_paid_signatures
        }
        "msi" {
            . (Join-Path $PSScriptRoot "..\msi-tools.ps1")
            Assert-MsiMatchesPayload $request.path $request.payload $request.scratch
        }
        default { throw "Unknown local signing operation" }
    }
    [Console]::Out.WriteLine('{"ok":true}')
}
catch {
    # Native tool errors can contain private selectors. Keep them off console,
    # CI logs, subprocess exceptions and saved receipts.
    [Console]::Error.WriteLine("Local Windows signing check failed. No automatic retry is permitted.")
    exit 1
}
