# Icon Resources

## Tray Icons
- `tray-connected.svg` — Blue LT badge (agent connected to hub)
- `tray-disconnected.svg` — Grey LT badge (agent disconnected)
- `tray-error.svg` — Red LT badge (agent error state)

The current tray runtime uses H.NotifyIcon's generated icon source so the
connected and disconnected indicators are deterministic in every release
payload. These SVGs remain the design sources for a future artwork-based icon;
switching back to file-backed icons requires committing and validating the
actual multi-resolution ICO payloads.

## MSIX Visual Assets
The following placeholder PNGs are needed for Package.appxmanifest:
- `Square44x44Logo.png` — 44x44 app icon
- `Square150x150Logo.png` — 150x150 tile icon
- `Wide310x150Logo.png` — 310x150 wide tile
- `StoreLogo.png` — 50x50 store logo

Generate these from the SVG source on Windows using ImageMagick or VS asset generator.
