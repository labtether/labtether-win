# LabTether Windows Agent

The Windows system tray agent for [LabTether](https://labtether.com).

## Requirements

- Visual Studio 2022+ with .NET 8 and Windows App SDK workload
- Windows 10 version 1809+ (build 17763)

## Build

The Windows agent bundles the Go `labtether-agent` binary. The build downloads it from the main repo's GitHub Releases based on the version pinned in `AGENT_VERSION`.

### Download agent binary

```powershell
./scripts/download-agent.ps1
```

### Build (development)

```bash
dotnet build src/LabTetherAgent/LabTetherAgent.csproj
```

### Build MSIX (release)

```powershell
./scripts/build-msix.ps1 -Arch x64
./scripts/build-msix.ps1 -Arch arm64
```

## Test

```bash
dotnet test tests/LabTetherAgent.Tests/
```

## Architecture

The tray app is a thin native WinUI 3 wrapper around the Go `labtether-agent.exe` binary. It manages the Go process lifecycle, communicates via the localhost HTTP API (`/agent/status`, `/agent/info`), and provides:

- System tray icon with status flyout
- Enrollment wizard
- Settings management with Windows Credential Manager
- Live log viewer
- Pop-out metrics window
- Windows toast notifications
- Hyper-V VM status and Windows Update awareness

This mirrors the architecture of the macOS menu bar agent (`mac-agent`).

## Project Structure

```
src/LabTetherAgent/
├── App/           # Application entry, single instance, state
├── Api/           # HTTP client for Go agent localhost API
├── Process/       # Go binary lifecycle management
├── Services/      # Connection testing, diagnostics, notifications
├── Settings/      # Config, credentials, environment builder
├── State/         # Observable status models
├── Presentation/  # ViewModels (MVVM)
├── Components/    # Reusable UI components
├── Views/         # Windows and pages
└── Resources/     # Icons, strings, fonts
```
