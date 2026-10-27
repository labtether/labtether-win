<div align="center">

<img src=".github/logo.svg" alt="LabTether" width="120" />

# LabTether for Windows

**A system tray app that connects your Windows machines to your [LabTether](https://github.com/labtether/labtether) hub.**

[![Windows](https://img.shields.io/badge/Windows-10+-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)

</div>

Install it and your Windows PCs and servers show up in the hub next to
everything else. The tray icon handles enrollment, so there is no config file
to edit.

> **Early release.** Windows support in LabTether is partial: metrics and
> terminal work, and more is in progress. See
> [platform support](https://labtether.com/docs/reference/platform-support).

## What you get

- **Live metrics.** CPU, memory, disk and network, plus temperature when
  Windows has a readable sensor.
- **Terminal** from the hub. Remote desktop works through a supported local VNC
  server or a direct RDP connection that you set up.
- **Status in the tray.** Connection state and quick actions.
- **Windows services.** See and manage them from the hub.
- **Hyper-V.** VM status and management on Hyper-V hosts.
- **Windows Update.** See pending updates and start installs from the hub.

## Install

1. Download `labtether-agent-win-x64.msi` from
   [Releases](https://github.com/labtether/labtether-win/releases/latest).
2. Run it. It installs for your user only and needs no administrator rights.
3. Open **LabTether Agent** from the Start menu. The tray icon walks you
   through connecting to your hub.

You need Windows 10 or newer (x64), a running hub, and an enrollment token from
the hub (**Add Device**). To upgrade, quit the tray app and run the newer MSI;
your settings and enrollment stay.

Prefer no installer? Each release also has a portable ZIP. To check either
download's checksum and signature first, see
[docs/verify-download.md](docs/verify-download.md).

## Uninstall

1. Exit the tray app.
2. Installer: uninstall **LabTether Agent** in **Settings > Apps**. It also
   removes the hub's SSH key line the agent added to
   `%USERPROFILE%\.ssh\authorized_keys`. ZIP: turn off **Start at login** in
   the app's settings, remove the SSH key line from the extracted folder, then
   delete the folder:

   ```powershell
   $env:LABTETHER_TOKEN_FILE = "$env:LOCALAPPDATA\LabTether\agent-token"
   .\Assets\labtether-agent.exe ssh-key remove
   ```

3. Remove the device in the hub.

The agent core in 1.7.0 cannot remove that SSH key line. With 1.7.0, delete
the hub's key from `authorized_keys` by hand.

To remove all local data too, delete `%LOCALAPPDATA%\LabTether` and the
LabTether entries in Windows Credential Manager.

## Troubleshooting

- **No tray icon.** Check that the app is running and look in the tray overflow.
- **Can't connect.** Check that the hub URL opens from this machine and that
  the enrollment token is still valid.
- **Moving to another hub.** A token works only with the hub that issued it,
  so Settings keeps the current hub. Choose **Setup / Re-enroll** in the tray
  menu and use an enrollment token from the new hub.
- **Agent logs.** Choose **View Logs** in the tray menu.

## How it works

The app wraps the same Go agent core used on Linux in a native .NET tray app.
It keeps one encrypted WebSocket connection open to your hub, stores its
credentials in Windows Credential Manager, and reconnects on its own.

The app runs for the signed-in user and does not install a Windows service.
For a server without a signed-in user, use the standalone
[LabTether agent](https://github.com/labtether/labtether-agent), which runs as
a service.

To run a second copy with its own settings and credentials, see
[docs/separate-state.md](docs/separate-state.md).

## Build from source

You need Visual Studio 2026 with the .NET 10 SDK, Go 1.27.3 and a sibling
checkout of [labtether-agent](https://github.com/labtether/labtether-agent).

```powershell
git clone https://github.com/labtether/labtether-agent ..\labtether-agent
bash .\scripts\build-bundled-agent.sh --version dev
msbuild src\LabTetherAgent\LabTetherAgent.csproj -t:Build -p:Configuration=Release -p:Platform=x64
.\scripts\build-unpackaged.ps1 -Arch x64 -OutputDir build
```

Building the MSI and other details: [docs/building.md](docs/building.md).

## Contributing

If you work with an AI coding agent, point it at [AGENTS.md](AGENTS.md) first.
The signed release process is in [docs/releasing.md](docs/releasing.md).
Changes are in [CHANGELOG.md](CHANGELOG.md).

## Help

- [Docs](https://labtether.com/docs) and [GitHub Discussions](https://github.com/labtether/labtether/discussions)
- Security issues: report privately as described in [SECURITY.md](SECURITY.md)
- Related: [LabTether hub](https://github.com/labtether/labtether),
  [Linux and Windows agent](https://github.com/labtether/labtether-agent),
  [Mac app](https://github.com/labtether/labtether-mac)

## License

Source available, all rights reserved. Copyright 2026 LabTether. You may view
and study this code, but you may not copy, modify, distribute or use it without
written permission. See [LICENSE](LICENSE).
