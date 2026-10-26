<div align="center">

<img src=".github/logo.svg" alt="LabTether" width="120" />

</div>

# LabTether Windows Agent

A native system tray app that connects your Windows machines to your [LabTether](https://labtether.com) hub -- telemetry, remote access, and actions from the notification area.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Windows](https://img.shields.io/badge/Windows-10+-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)

<!-- TODO: Add screenshot of system tray agent -->

---

## Install

### Installer (recommended)

Download `labtether-agent-win-x64.msi` from
[Releases](https://github.com/labtether/labtether-win/releases/latest) and run
it. It installs for the current user only, without administrator rights, into
`%LOCALAPPDATA%\Programs\LabTether`, adds **LabTether Agent** to the Start
menu, and appears in **Settings > Apps** for uninstall. Running a newer MSI
upgrades in place and keeps your settings and enrollment. Quit the tray app
before upgrading so its files are not in use.

To check the download first, compare it with
`labtether-agent-win-x64.msi.sha256` and its signature:

```powershell
$expected = ((Get-Content .\labtether-agent-win-x64.msi.sha256 -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash .\labtether-agent-win-x64.msi -Algorithm SHA256).Hash -ne $expected) { throw "Checksum mismatch" }
(Get-AuthenticodeSignature .\labtether-agent-win-x64.msi).Status   # Valid
```

### Portable ZIP

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

Requires Visual Studio 2026 with the .NET 10 SDK selected by `global.json`
and Windows App SDK build tools, plus Go 1.27.2 and a sibling
`labtether-agent` checkout. Release builds fail closed if
the matching Go child or its version marker is missing.

```powershell
git clone https://github.com/labtether/labtether-agent ..\labtether-agent
bash .\scripts\build-bundled-agent.sh --version dev
msbuild src\LabTetherAgent\LabTetherAgent.csproj -t:Build -p:Configuration=Release -p:Platform=x64
.\scripts\build-unpackaged.ps1 -Arch x64 -OutputDir build
```

`build-unpackaged.ps1` produces the same self-contained folder layout used by
the local release lane. Add `-Msi` to also build the per-user installer
(`installer/`, WiX Toolset v5) next to that folder, or run
`.\scripts\build-msi.ps1 -PayloadDir build\win-x64 -OutputPath build\labtether-agent-win-x64.msi`.
Both check that the MSI holds exactly the payload's files. The legacy
`build-msix.ps1` name remains as a compatibility wrapper, but it does not
produce an MSIX.

`AGENT_VERSION` names the Go agent release used by
`scripts/download-agent.ps1` for local builds. Official releases always bundle
the `labtether-agent` build from the tag that matches the wrapper tag.

For most users, download the pre-built signed installer from [Releases](https://github.com/labtether/labtether-win/releases/latest) instead.

### Maintainer release lane

The tag-triggered GitHub workflow verifies clean matching wrapper and agent
tags, tests, publishes, builds and checks the MSI, and validates the payload.
It intentionally has read-only permissions: it does not receive signing
material, sign bytes, keep a release archive or installer, attest an archive,
or upload a release.

Official Windows releases use a split local process:

1. On Windows, run `scripts/release/prepare-unsigned-windows-release.ps1` from
   clean matching tagged wrapper and agent checkouts. It builds into an
   external current-user-only staging directory and emits one unsigned archive
   with embedded source provenance.
2. Transfer only that unsigned archive to the Mac and run
   `scripts/release/sign-windows-release.sh --unsigned-archive PATH
   --confirm-sign TAG`, where `TAG`
   is the same strict `vX.Y.Z` tag. The script reads the local
   certificate path and password silently from `/dev/tty`, consumes the
   certificate in place through a file descriptor, Authenticode-signs the three
   authored PE files with `osslsigncode`, and emits exactly the release ZIP and
   its checksum in a mode-0700 external directory.
3. Transfer only those two signed assets to Windows and run
   `scripts/release/verify-signed-windows-release.ps1` with
   `-UnsignedMsiOutputPath <external dir>\labtether-agent-win-x64-unsigned.msi`.
   It re-extracts the ZIP, checks source provenance and checksums, validates
   timestamped Authenticode signatures, runs the signed child and WinUI
   probes, and then builds the per-user MSI from exactly that verified signed
   payload.
4. Transfer only that unsigned MSI to the Mac and run
   `scripts/release/sign-windows-release.sh --signed-assets DIR
   --unsigned-msi PATH --confirm-sign TAG`. It re-verifies the signed ZIP,
   signs the MSI with the same certificate, checks both use one signer, and
   emits the four release assets: ZIP, MSI and their checksums.
5. Transfer those four assets to Windows and run
   `verify-signed-windows-release.ps1` again without `-UnsignedMsiOutputPath`.
   It repeats every ZIP check and also verifies the MSI checksum, its
   timestamped signature from the payload signer, and that an administrative
   image of the MSI holds exactly the verified payload. Keep its proof file
   local; it is not a release asset.
6. Return the four assets and that proof to the Mac, then run
   `scripts/release/publish-windows-release.sh --confirm-draft TAG`. It repeats
   the Mac verification, requires the matching Windows proof for both the ZIP
   and the MSI, refuses to overwrite an existing release, and uploads exactly
   the four assets to a draft. It verifies every GitHub asset name, size,
   state, and SHA-256 digest, then exits while the release is still a draft.
7. Inspect that draft independently. In a new invocation, run
   `scripts/release/publish-windows-release.sh --confirm-publish TAG`. It
   repeats every local check and performs a fresh GitHub readback of the
   existing draft before publishing it. The script rejects both confirmations
   in one invocation, so no command can create and publish a release together.

No signing file, private key, password, encoded secret, local certificate path,
or Windows verification proof belongs in Git, GitHub Actions, build artifacts,
caches, or release uploads. Only the public signer information inherently
embedded in the verified signed PE and MSI files leaves the local signing
lane.

---

## Separate application state

The normal launch still uses the installed user's LabTether settings and Windows
credential vault. To run the same app with separate state, pass one absolute
local directory:

```powershell
LabTetherAgent.exe --state-directory 'D:\LabTetherState\my-instance'
```

The directory owns settings, current-user DPAPI credentials, setup transactions,
child identity and tokens. It must be outside the installed state directory and
cannot use junctions or symbolic links. Do not share a directory between users.
Each directory has its own activation key and exact child recovery record.
Start at Login and toast registration are unavailable for separate state; the
installed app's registrations and saved credentials are not changed. The normal
setup and Hub connection flow is used. Quit stops the owned child; durable state
stays in the chosen directory for later use. Removing that directory is a separate
user action. An older child without an ownership record is never killed just
because it runs the same executable.

## How It Works

The Windows agent runs as a system tray application with an optional Windows Service for unattended operation. On launch, it establishes a persistent WebSocket connection to your hub and begins reporting system telemetry. The hub can then issue commands back -- opening terminal sessions, managing services, querying Hyper-V status, or triggering Windows Update scans -- all through the encrypted channel.

The agent handles enrollment, credential storage (Windows Credential Manager), and automatic reconnection. It can be installed as a Windows Service for headless servers.

---

## Uninstall

1. Exit the tray app.
2. If you used the installer, uninstall **LabTether Agent** from
   **Settings > Apps**; it also removes the Start at Login entry. For the ZIP,
   disable **Start at login** in LabTether Agent settings first, then delete
   the directory where you extracted it.
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
- **Community** -- [GitHub Discussions](https://github.com/labtether/labtether/discussions)
- **Security** -- report vulnerabilities privately, see [SECURITY.md](SECURITY.md)
- **Website** -- [labtether.com](https://labtether.com)

## License

Source-available, all rights reserved. Copyright 2026 LabTether. You may view
and study this code, but you may not copy, modify, distribute or use it without
written permission. See [LICENSE](LICENSE).
