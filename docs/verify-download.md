# Check a download before you run it

## Checksum

Download `labtether-agent-win-x64.msi` and its `.sha256` file from
[Releases](https://github.com/labtether/labtether-win/releases/latest). In
PowerShell, in the download folder:

```powershell
$file = "labtether-agent-win-x64.msi"
$expected = ((Get-Content ".\$file.sha256" -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash ".\$file" -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

For the ZIP, set `$file` to `labtether-agent-win-x64.zip`. A release without
an installer, such as 2.0.0, has only the ZIP.

## Signatures

The release notes say whether a release is code-signed. An unsigned release
has no signatures to check.

For a signed release, check that the installer carries a valid, timestamped
signature:

```powershell
$signature = Get-AuthenticodeSignature .\labtether-agent-win-x64.msi
if ($signature.Status -ne "Valid" -or $null -eq $signature.TimeStamperCertificate) {
  throw "Invalid or untimestamped Authenticode signature on the installer"
}
$signature.SignerCertificate.Subject
```

Releases from 2.0.1 on are signed by `CN=Watari Labs Pty Ltd` with a
certificate from `Certum Code Signing 2021 CA`, timestamped by Certum.

For the ZIP of a signed release, extract the app and check that all three
LabTether programs carry the same valid, timestamped signature:

```powershell
Expand-Archive .\labtether-agent-win-x64.zip .\LabTetherAgent
$payloads = @(
  ".\LabTetherAgent\LabTetherAgent.exe",
  ".\LabTetherAgent\LabTetherAgent.dll",
  ".\LabTetherAgent\Assets\labtether-agent.exe"
)
$signerThumbprint = $null
foreach ($payload in $payloads) {
  $signature = Get-AuthenticodeSignature $payload
  if ($signature.Status -ne "Valid" -or $null -eq $signature.TimeStamperCertificate) {
    throw "Invalid or untimestamped Authenticode signature: $payload"
  }
  if ($null -eq $signerThumbprint) { $signerThumbprint = $signature.SignerCertificate.Thumbprint }
  if ($signature.SignerCertificate.Thumbprint -ne $signerThumbprint) { throw "Signer mismatch: $payload" }
}
```

If the checks pass, open the installer, or run `LabTetherAgent.exe` from the
extracted folder.
