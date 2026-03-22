# LabTether Windows Agent

A system tray app that connects your Windows machines to your [LabTether](https://labtether.com) hub — telemetry, remote access, and actions from the notification area.

## Install

Download **LabTether Agent** from [Releases](https://github.com/labtether/labtether-win/releases/latest) and run the installer. The system tray icon walks you through hub enrollment.

For detailed setup, see the [full guide](https://labtether.com/docs/wiki/agents/windows).

## What It Does

- **System telemetry** — CPU, memory, disk, network, and temperature. Reported every heartbeat.
- **Remote terminal & desktop** — Open a shell or desktop session from the LabTether console. No RDP config needed.
- **System tray status** — Connection state and quick actions from the notification area.
- **Windows services** — Monitor and manage Windows services from the dashboard.
- **Hyper-V monitoring** — VM status and management for Hyper-V hosts.
- **Windows Update** — See pending updates, trigger installs from the console.

## Build From Source

Requires Visual Studio 2022+ with .NET 8 and Windows App SDK workload.

```powershell
dotnet build src\LabTetherAgent\LabTetherAgent.csproj
```

Most users should grab the pre-built installer from [Releases](https://github.com/labtether/labtether-win/releases/latest).

## Links

| | |
|---|---|
| **LabTether Hub** | [github.com/labtether/labtether](https://github.com/labtether/labtether) |
| **Docs** | [labtether.com/docs](https://labtether.com/docs) |
| **Website** | [labtether.com](https://labtether.com) |

## License

Copyright 2026 LabTether. All rights reserved. See [LICENSE](LICENSE).
