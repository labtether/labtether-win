# Changelog

## [Unreleased]

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
