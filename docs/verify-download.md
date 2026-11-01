# Check a download before you run it

## Checksum

Download `labtether-agent-win-x64.zip` and its `.sha256` file from
[Releases](https://github.com/labtether/labtether-win/releases/latest). In
PowerShell, in the download folder:

```powershell
$expected = ((Get-Content .\labtether-agent-win-x64.zip.sha256 -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash .\labtether-agent-win-x64.zip -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

## Signatures

v1.7.1 is not code-signed, so it has no signatures to check. The release notes
say whether a release is signed.

For a signed release, extract the app and check that all three LabTether
programs carry the same valid, timestamped signature:

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

If the checks pass, run `LabTetherAgent.exe` from the extracted folder.
