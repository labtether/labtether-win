# Icon Resources

## Tray Icons
- `tray-connected.svg` — Blue LT badge (agent connected to hub)
- `tray-disconnected.svg` — Grey LT badge (agent disconnected)
- `tray-error.svg` — Red LT badge (agent error state)

## Converting to .ico (required for Windows)
On Windows, convert SVGs to multi-resolution .ico files:
```powershell
# Using ImageMagick:
magick tray-connected.svg -define icon:auto-resize=16,32,48 tray-connected.ico
magick tray-disconnected.svg -define icon:auto-resize=16,32,48 tray-disconnected.ico
magick tray-error.svg -define icon:auto-resize=16,32,48 tray-error.ico
```

## MSIX Visual Assets
The following placeholder PNGs are needed for Package.appxmanifest:
- `Square44x44Logo.png` — 44x44 app icon
- `Square150x150Logo.png` — 150x150 tile icon
- `Wide310x150Logo.png` — 310x150 wide tile
- `StoreLogo.png` — 50x50 store logo

Generate these from the SVG source on Windows using ImageMagick or VS asset generator.
