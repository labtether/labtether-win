<div align="center">

<img src=".github/logo.svg" alt="LabTether" width="120" />

</div>

# LabTether Windows Agent

A native system tray app that connects your Windows machines to your [LabTether](https://labtether.com) hub -- telemetry, remote access, and actions from the notification area.

[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Windows](https://img.shields.io/badge/Windows-10+-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)

<!-- TODO: Add screenshot of system tray agent -->

---

## Install

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

Launch `LabTetherAgent.exe`; the system tray icon walks you through hub enrollment.

For detailed setup, see the [Windows agent setup guide](https://labtether.com/docs/install-upgrade/agent-install-commands-by-os).

---

## What It Does

- **System telemetry** -- CPU, memory, disk, and network reported to your hub every heartbeat, with temperature included when Windows exposes a usable sensor source.
- **Remote access** -- Terminal sessions through the bundled agent. Remote desktop requires a supported local VNC server or a separately configured direct RDP connection.
- **System tray status** -- Connection state and quick actions from the notification area.
- **Windows services** -- Monitor and manage Windows services from the dashboard.
- **Hyper-V monitoring** -- VM status and management for Hyper-V hosts.
- **Windows Update** -- View pending updates and trigger installations from the console.

---

## Requirements

- Windows 10 or later (x64)
- A running [LabTether hub](https://github.com/labtether/labtether) to connect to
- An enrollment token generated from the hub console

---

## Build From Source

Requires Visual Studio 2022+ with .NET 8 and the Windows App SDK workload,
plus Go and a sibling `labtether-agent` checkout. Release builds fail closed if
the matching Go child or its version marker is missing.

```powershell
git clone https://github.com/labtether/labtether-agent ..\labtether-agent
bash .\scripts\build-bundled-agent.sh --version dev
msbuild src\LabTetherAgent\LabTetherAgent.csproj -t:Build -p:Configuration=Release -p:Platform=x64
.\scripts\build-unpackaged.ps1 -Arch x64 -OutputDir build
```

`build-unpackaged.ps1` produces the same self-contained folder layout used by
the local release lane. The legacy
`build-msix.ps1` name remains as a compatibility wrapper, but it does not
produce an MSIX.

For most users, download the pre-built signed archive from [Releases](https://github.com/labtether/labtether-win/releases/latest) instead.

### Maintainer release lane

The tag-triggered GitHub workflow verifies clean matching wrapper and agent
tags, tests, publishes, and runs the WinUI probe. It intentionally has read-only
permissions: it does not receive signing material, sign bytes, create a release
archive, attest an archive, or upload a release.

Official Windows releases use a split local process:

1. On Windows, run `scripts/release/prepare-unsigned-windows-release.ps1` from
   clean matching tagged wrapper and agent checkouts. It builds into an
   external current-user-only staging directory and emits one unsigned archive
   with embedded source provenance.
2. Transfer only that unsigned archive to the Mac and run
   `scripts/release/sign-windows-release.sh --confirm-sign TAG`, where `TAG`
   is the same strict `vX.Y.Z` tag. The script reads the local
   certificate path and password silently from `/dev/tty`, consumes the
   certificate in place through a file descriptor, Authenticode-signs the three
   authored PE files with `osslsigncode`, and emits exactly the release ZIP and
   its checksum in a mode-0700 external directory.
3. Transfer only those two signed assets to Windows and run
   `scripts/release/verify-signed-windows-release.ps1`. It re-extracts the ZIP,
   checks source provenance and checksums, validates timestamped Authenticode
   signatures, and runs the signed child and WinUI probes. Keep its proof file
   local; it is not a release asset.
4. Return the two assets and proof to the Mac, then run
   `scripts/release/publish-windows-release.sh --confirm-publish TAG`. It
   repeats the Mac verification, requires the matching Windows proof, refuses
   to overwrite an existing release, and uploads exactly the ZIP and checksum
   to a draft. It verifies both GitHub asset names, sizes, states, and SHA-256
   digests before publishing the already-inspected draft.

No signing file, private key, password, encoded secret, local certificate path,
or Windows verification proof belongs in Git, GitHub Actions, build artifacts,
caches, or release uploads. Only the public signer information inherently
embedded in the verified signed PE files leaves the local signing lane.

---

## How It Works

The Windows agent runs as a system tray application with an optional Windows Service for unattended operation. On launch, it establishes a persistent WebSocket connection to your hub and begins reporting system telemetry. The hub can then issue commands back -- opening terminal sessions, managing services, querying Hyper-V status, or triggering Windows Update scans -- all through the encrypted channel.

The agent handles enrollment, credential storage (Windows Credential Manager), and automatic reconnection. It can be installed as a Windows Service for headless servers.

---

## Uninstall

1. Disable **Start at login** in LabTether Agent settings, then exit the tray app.
2. Delete the directory where you extracted the release ZIP.
3. Remove the agent from your hub's asset list via the console.

For complete local-data removal, also delete `%LOCALAPPDATA%\LabTether` and
the LabTether credentials from Windows Credential Manager.

---

## Troubleshooting

- **System tray icon not appearing** -- Check that the app is running and not hidden in the overflow area.
- **Connection issues** -- Verify the hub URL is reachable and that your enrollment token is valid.
- **Service mode** -- If running as a Windows Service, check Event Viewer for agent logs.

---

## Links

- **LabTether Hub** -- [github.com/labtether/labtether](https://github.com/labtether/labtether)
- **Linux Agent** -- [github.com/labtether/labtether-agent](https://github.com/labtether/labtether-agent)
- **macOS Agent** -- [github.com/labtether/labtether-mac](https://github.com/labtether/labtether-mac)
- **Documentation** -- [labtether.com/docs](https://labtether.com/docs)
- **Website** -- [labtether.com](https://labtether.com)

## License

Copyright 2026 LabTether. All rights reserved. See [LICENSE](LICENSE).
