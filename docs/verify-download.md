# Check a download before you install it

## Installer (MSI)

To check the download first, compare it with
`labtether-agent-win-x64.msi.sha256` and its signature:

```powershell
$expected = ((Get-Content .\labtether-agent-win-x64.msi.sha256 -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash .\labtether-agent-win-x64.msi -Algorithm SHA256).Hash -ne $expected) { throw "Checksum mismatch" }
(Get-AuthenticodeSignature .\labtether-agent-win-x64.msi).Status   # Valid
```

## Portable ZIP

Download `labtether-agent-win-x64.zip` and its `.sha256` file from
[Releases](https://github.com/labtether/labtether-win/releases/latest). Verify
the checksum, extract the app, and verify all three LabTether-authored PE
signatures:

```powershell
$expected = ((Get-Content .\labtether-agent-win-x64.zip.sha256 -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash .\labtether-agent-win-x64.zip -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Checksum mismatch" }
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

If the checks pass, run the MSI, or `LabTetherAgent.exe` from the extracted folder.
