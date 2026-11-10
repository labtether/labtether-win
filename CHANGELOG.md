# Changelog

All notable changes to the LabTether Windows Agent are listed here. Versions
follow [Semantic Versioning](https://semver.org/).

## [2.0.0] - unreleased

Changes since v1.7.1. This release matches LabTether 2.0.

### Added
- Setup can trust a Hub that uses its own certificate, so no CA file is
  needed for a home Hub. When Windows does not know the Hub's CA, setup shows
  "This Hub uses its own certificate" with the CA's SHA-256 fingerprint, the
  same value the console shows under Add device. Trust this Hub saves that CA
  for this Hub and then connects with the certificate check on; Cancel saves
  nothing. An expired certificate or one for another address is an error and
  cannot be trusted this way. If the CA later differs from the one the device
  enrolled with, setup says "This Hub's certificate changed" and asks again.
- A Hub trusted this way is then checked against that saved CA only, not
  against Windows' own certificate list. A server with a publicly trusted
  certificate for the same address is refused. If the saved CA file is
  missing or can't be read, the agent is not started and the flyout, the
  notification and the log say so. A Hub with a publicly trusted certificate
  and no CA file works as before. Needs the bundled agent from
  labtether-agent `54d2e6a` or newer.
- A CA certificate file chosen by hand under Advanced is treated the same way
  from the moment it has verified the Hub on its own, and stays so. Settings
  checks a newly chosen file with the Hub before saving: a file that does not
  match is refused with "This CA certificate does not match this Hub's
  certificate." and the earlier setting stays. If such a file later goes
  missing or is replaced by another CA, the agent is not started and the app
  says so; it never goes back to also trusting Windows' own list.
- A CA file saved by an earlier version was never checked. The app checks it
  at each start and after each reconnect until it has verified the Hub. Until
  then the device keeps working as it did in 1.x (that file and Windows' own
  list). If the Hub answers and the file does not match, Settings shows "This
  CA certificate does not match this Hub's certificate." with **Remove**.
- Maintainers can build an unsigned release ZIP with a hand-started hosted
  workflow, `unsigned-release-build.yml`. It signs nothing and uploads
  nothing to a release.

### Changed
- Start at sign-in turns on when the first setup succeeds, so the device is
  back online after a restart. Settings can turn it off; re-enroll keeps the
  current choice.
- The MSI starts the app when an install or upgrade ends. For a scripted
  install, `msiexec /i labtether-agent-win-x64.msi /qn LAUNCH=0` leaves it
  closed.
- The app has the LabTether 2.0 look in light and dark, following Windows,
  with Windows' own high contrast colours under a contrast theme. Rose is
  used only for each window's main button; switches and checkboxes use the
  text colour.
- The tray icon is the LabTether mascot drawn in one colour for light and
  dark taskbars. An amber dot means a warning alert, a red dot a critical
  alert or a stopped agent, and a ring with a dimmed glyph means offline.
  The tooltip says what happened and when, for example "Offline since 21:12".
- The flyout opens above the tray and closes when it loses focus. It shows
  problems first, then CPU, memory and disk as thin bars that turn amber
  from 85%.
- Setup is one screen: Hub URL, token type and token, with CA certificate
  file, Skip certificate check, Device ID and Group ID under Advanced.
- For a device that enrolled with one Hub CA (trusted in setup, or a CA
  file with one certificate), Settings refuses a different CA certificate or
  Skip certificate check while its token is saved. Re-enroll in the tray
  menu changes the Hub or its certificate and always uses a new token.
- Settings are label-left rows under Connection, General and Docker, and
  the tray menu, Settings and setup use sentence case ("Open console",
  "Re-enroll…", "Start at sign-in").
- The app, its MSI and the bundled agent core are version 2.0.0. The bundled
  agent is `labtether-agent v2.0.0`, and `scripts/download-agent.ps1`
  fetches v2.0.0 by default.
- The README and docs/verify-download.md no longer name one release as
  unsigned. The release notes say whether a release is code-signed.

### Fixed
- `scripts/download-agent.ps1` without `-Version` works in Windows
  PowerShell 5.1. It failed before downloading because it read
  `AGENT_VERSION` with a `Join-Path` form that needs PowerShell 7.
- The README describes the ZIP that releases ship and how to upgrade it.
- Open Console in the tray menu and the flyout opens the web console. With
  the Hub URL that Add device shows (the Hub API port) it opened a page of
  Hub API JSON. The app now asks the Hub for its console address and opens
  that.
- The tray menu is dark on a dark taskbar and light on a light one. It was
  always light.
- Cancel and Save in Settings close the window.
- In setup, the space under the Hub URL closes again once a wrong URL is
  corrected.
- Before setup is finished, the tray, flyout, pop-out and About say "Not set
  up" instead of an offline "localhost" Hub, and Open console is off.
- Windows notifications appear. The installed app showed none: the Windows
  App SDK could not register this self-contained app. Notifications now
  come from LabTether Agent with its icon.
- "Offline since" appears after the Hub has been unreachable for 20 seconds,
  and "Reconnected" only after it. Saving Settings or re-enrolling no longer
  shows either.
- An alert notification appears once per firing, not every five minutes.
- After an MSI upgrade closes the app, Windows starts it again.
- Under a contrast theme, buttons, lists and switches use the theme's
  colours.
- Save in Settings turns on while you type in a text box.
- The logs window shows the whole time, and screen readers read each log
  line.
- A reinstall over a kept setup turns Start at sign-in on again (the
  uninstall removed it). The MSI marks each fresh install; the app applies
  this once, so turning it off in Settings stays off.
- Before setup, Settings shows an empty Hub URL with a hint instead of the
  placeholder `wss://localhost:8443/ws/agent`.
- Under a contrast theme a warning alert shows as a ring and a critical alert
  as a dot; both use the theme's highlight colour.
- When the Hub refuses an enrollment (for example a device ID it already has),
  setup shows the Hub's reason. Without one it says the Hub did not accept the
  device with this token.
- The default Docker endpoint reaches the agent as
  `npipe:////./pipe/docker_engine`, the form it accepts. The app passed
  `\\.\pipe\docker_engine`, which the agent rejected, and it then looked for
  a Unix socket instead.

## [1.7.1] - 2026-10-31

Changes since the last public release, v1.4.0. v1.7.0 was tagged but never
released.

### Added
- Source for a per-user installer, `labtether-agent-win-x64.msi`. It needs no
  administrator rights, installs to `%LOCALAPPDATA%\Programs\LabTether`, adds
  a Start menu shortcut and an uninstall entry in Settings > Apps, and
  upgrades in place. The MSI is built only for signed releases, so the
  unsigned 1.7.1 release ships only the ZIP.
- When the agent stops and cannot be restarted, the tray icon turns red, its
  tooltip says so, and a notification appears. The reason is in View Logs.
- When the agent cannot connect to the Hub, the tray window and pop-out say
  why, for example "Can't reach the Hub" or "The Hub's certificate isn't
  trusted", instead of "Connecting".
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
- `scripts/download-agent.ps1` fetches an agent release that exists (v1.7.1 by
  default) and writes the version marker the build checks.
- With Allow Remote Overrides off, settings pushed earlier by the Hub are
  removed on the next start, so the agent runs the values shown in Settings.
- The tray flyout no longer has Hyper-V and Windows Update cards. The agent
  core never reported their data, so they could not show anything.
- The README no longer describes a Windows service mode that the app does not
  have.

### Security
- Signing happens only on the maintainer's machine. Hosted CI never holds
  signing credentials. The 1.7.1 ZIP was built by a hand-started hosted
  workflow and is not code-signed.
- Release ZIPs record the exact source tags they were built from, and the MSI
  build checks that the installer holds exactly the release files.
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
  `authorized_keys`, and Setup keeps the agent's record of that line.

## Early development

Historical scaffold notes. Releases ship as a ZIP; the MSIX and
`.appinstaller` items below never shipped.

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
