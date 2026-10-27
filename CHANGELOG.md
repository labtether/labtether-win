# Changelog

All notable changes to the LabTether Windows Agent are listed here. Versions
follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Security
- Settings no longer saves a different Hub URL while a token for the current
  Hub is saved, because the agent would send that token to the new address.
  Use Setup / Re-enroll with an enrollment token from the new Hub. A Hub URL
  written another way for the same Hub, such as `https://` instead of `wss://`,
  still saves.
- A folder passed with `--state-directory` is created so that only you,
  SYSTEM and Administrators can open it. An existing folder that another user
  owns or can open is refused, because that user could change its settings.
  Move a refused instance to a new folder.
- An identity left in `C:\ProgramData\LabTether` by an older version is moved
  into the app only when no other user could have replaced it.
- Uninstalling the MSI removes the Hub's SSH key line that the agent added to
  `authorized_keys`, and Setup keeps the agent's record of that line. This
  needs an agent core newer than 1.7.0; with 1.7.0 the step does nothing and
  the uninstall finishes as before.

### Fixed
- With Allow Remote Overrides off, settings pushed earlier by the Hub are
  removed on the next start, so the agent runs the values shown in Settings.
- The tray flyout no longer has Hyper-V and Windows Update cards. The agent
  core never reported their data, so they could not show anything.
- The README no longer describes a Windows service mode that the app does not
  have.

## [1.7.0] - unreleased

Changes since the last public release, v1.4.0.

### Added
- A per-user installer, `labtether-agent-win-x64.msi`. It needs no
  administrator rights, installs to `%LOCALAPPDATA%\Programs\LabTether`, adds
  a Start menu shortcut and an uninstall entry in Settings > Apps, and
  upgrades in place. The portable ZIP is still published.
- When the agent stops and cannot be restarted, the tray icon turns red, its
  tooltip says so, and a notification appears. The reason is in View Logs.
- Launching with `--state-directory PATH` keeps that copy's settings,
  credentials and agent identity apart from the installed app.
- Hub URLs on a loopback address work, so a local tunnel to the Hub can be used.

### Changed
- Setup only saves a new Hub connection after the agent has an authenticated,
  durable connection. A failed setup keeps the previous one.
- The bundled agent core is built from the matching release tag and keeps its
  own identity and settings, separate from a standalone Windows service
  agent on the same PC.
- Start at Login points at the copy you are running. An entry left by an
  older copy in another folder shows as off until you turn it on again.
- Agent logs use bounded memory. Very long log lines are split, and exports
  never contain a secret that was split across lines.
- Numeric settings such as ports and intervals are checked before they are used.
- The app ships with and requires the patched .NET 10.0.14 runtime.
- The bundled agent core is built with Go 1.27.3.

### Fixed
- A plain `http://` or `ws://` Hub now connects instead of failing a TLS
  handshake.
- The tray app keeps running after onboarding or after its last window closes.
- A failed agent restart is reported instead of leaving the agent silently
  stopped, and an error in a window no longer closes the whole tray app.
- Stopping the agent cancels a pending crash restart.
- A stopped or replaced agent can no longer change the status shown in the tray.
- Status polling keeps working when several windows are open.
- The update check reads the right release feed.
- Hub URLs are validated more strictly, and console links are built safely.
- `scripts/download-agent.ps1` fetches an agent release that exists (v1.7.0 by
  default) and writes the version marker the build checks.

### Security
- Releases are signed on the maintainer's machine. Hosted CI only checks the
  tagged sources and never holds signing credentials.
- Release archives carry source provenance, and the MSI is checked to hold
  exactly the signed release payload.

## Early development

Historical scaffold notes. Releases ship as a ZIP and, from 1.7.0, a per-user
MSI; the MSIX and `.appinstaller` items below never shipped.

### Security
- Onboarding connection test now honors the explicit "Trust self-signed certificate" opt-in instead of silently accepting every TLS certificate. Self-signed homelab deployments must tick the new checkbox on Step 1 to complete onboarding; the flag is persisted to `AgentSettings.TlsSkipVerify`.

### Added
- Initial project scaffold
- WinUI 3 system tray app with Mica backdrop
- Go agent process management with crash restart
- Local API client with ETag caching and visibility-aware polling
- Settings with Windows Credential Manager storage
- Onboarding wizard (hub URL + token + identity)
- Flyout with metrics, alerts, Hyper-V, and Windows Update cards
- Settings, log viewer, pop-out, and about windows
- Toast notifications for connection and alert events
- Start at Login via MSIX startup task
- MSIX packaging with auto-update via .appinstaller
