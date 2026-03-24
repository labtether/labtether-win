# LabTether Windows Agent

A native system tray app that connects your Windows machines to your [LabTether](https://labtether.com) hub -- telemetry, remote access, and actions from the notification area.

[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Windows](https://img.shields.io/badge/Windows-10+-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)

<!-- TODO: Add screenshot of system tray agent -->

---

## Install

Download **LabTether Agent** from [Releases](https://github.com/labtether/labtether-win/releases/latest) and run the installer. The system tray icon walks you through hub enrollment.

For detailed setup, see the [Windows agent setup guide](https://labtether.com/docs/install-upgrade/agent-install-commands-by-os).

---

## What It Does

- **System telemetry** -- CPU, memory, disk, network, and temperature reported to your hub every heartbeat.
- **Remote access** -- Terminal and desktop sessions from the LabTether console. No RDP configuration needed.
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

Requires Visual Studio 2022+ with .NET 8 and Windows App SDK workload.

```powershell
dotnet build src\LabTetherAgent\LabTetherAgent.csproj
```

For most users, download the pre-built installer from [Releases](https://github.com/labtether/labtether-win/releases/latest) instead.

---

## How It Works

The Windows agent runs as a system tray application with an optional Windows Service for unattended operation. On launch, it establishes a persistent WebSocket connection to your hub and begins reporting system telemetry. The hub can then issue commands back -- opening terminal sessions, managing services, querying Hyper-V status, or triggering Windows Update scans -- all through the encrypted channel.

The agent handles enrollment, credential storage (Windows Credential Manager), and automatic reconnection. It can be installed as a Windows Service for headless servers.

---

## Uninstall

1. Exit LabTether Agent from the system tray.
2. Uninstall via **Settings > Apps > Installed apps** or **Add/Remove Programs**.
3. Remove the agent from your hub's asset list via the console.

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
