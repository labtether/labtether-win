# LabTether Windows Agent

The Windows system tray agent for [LabTether](https://labtether.com) — reports telemetry, executes actions, and enables remote access for your Windows machines.

## Install

Download **LabTether Agent** from [Releases](https://github.com/labtether/labtether-win/releases/latest) and run the installer. The system tray icon guides you through hub enrollment.

For detailed setup, see the [agent setup guide](https://labtether.com/docs/wiki/agents/windows).

## What It Does

- **System telemetry** — CPU, memory, disk, network, and temperature reported to your hub.
- **Remote access** — Terminal and desktop sessions from the LabTether console.
- **System tray status** — Connection state and quick actions from the notification area.
- **Windows services** — Monitor and manage Windows services remotely.
- **Hyper-V monitoring** — VM status and management for Hyper-V hosts.
- **Windows Update** — View pending updates and trigger installations from the console.

## Build From Source

Requires Visual Studio 2022+ with .NET 8 and Windows App SDK workload.

```powershell
dotnet build src/LabTetherAgent/LabTetherAgent.csproj
```

For most users, download the pre-built installer from [Releases](https://github.com/labtether/labtether-win/releases/latest) instead.

## Links

- **LabTether Hub** — [github.com/labtether/labtether](https://github.com/labtether/labtether)
- **Documentation** — [labtether.com/docs](https://labtether.com/docs)
- **Website** — [labtether.com](https://labtether.com)

## License

Copyright 2026 LabTether. All rights reserved. See [LICENSE](LICENSE).
