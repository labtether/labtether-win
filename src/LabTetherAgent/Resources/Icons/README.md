# Icon Resources

## Tray icon
The tray icon is drawn at runtime by `Views/TrayIcon/TrayGlyph.cs`: the
mascot in one colour (the theme's text colour for a light or dark taskbar),
at the tray size Windows asks for, with the face and the gap around the status
dot cut out so the taskbar shows through. A warning alert adds an amber dot, a
critical alert or a stopped agent a red dot, and offline a ring with the glyph
dimmed. The colours come from the generated tokens in `Theme/LabTetherTheme.xaml`.

## Mascot
`logo.svg` is the full mascot. The app draws the simplified mascot from the
Hub's `web/console/app/icon.svg` in XAML (`Components/Mascot.xaml`) for the
flyout, setup, title bars and About.

## MSIX Visual Assets
The following placeholder PNGs are needed for Package.appxmanifest:
- `Square44x44Logo.png`: 44x44 app icon
- `Square150x150Logo.png`: 150x150 tile icon
- `Wide310x150Logo.png`: 310x150 wide tile
- `StoreLogo.png`: 50x50 store logo

Generate these from the SVG source on Windows using ImageMagick or VS asset generator.
