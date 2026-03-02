# Verbinal — Windows / Microsoft Store asset pack

This pack is based on the approved **Portal Ring** logo and prepared for Windows app packaging and Microsoft Store submission.

## Included folders

- **Brand/** — master logo files, transparent icon, plated store icon, and ICO files
- **Windows_Current_Assets/** — current Windows asset names such as `AppList.targetsize-*`, `StoreLogo.scale-*`, `MedTile.scale-*`, `SplashScreen.scale-*`
- **VisualStudio_Legacy_Assets/** — Visual Studio / Package.appxmanifest-style names such as `Square44x44Logo.scale-*`, `Square150x150Logo.scale-*`, `Wide310x150Logo.scale-*`, `Square310x310Logo.scale-*`, `StoreLogo.scale-*`, `SplashScreen.scale-*`
- **Store_Listing/** — listing image for Partner Center (`Verbinal_AppTileIcon_300x300.png`)

## Recommended usage

### For Microsoft Store / modern packaged apps
Use the files in **Windows_Current_Assets/**.

### For Visual Studio packaging projects
Use the files in **VisualStudio_Legacy_Assets/** and reference the base names in your manifest, for example:
- `Square44x44Logo="Assets\Square44x44Logo.png"`
- `Square150x150Logo="Assets\Square150x150Logo.png"`
- `Wide310x150Logo="Assets\Wide310x150Logo.png"`
- `Square310x310Logo="Assets\Square310x310Logo.png"`
- `StoreLogo="Assets\StoreLogo.png"` (where applicable)
- `SplashScreen Image="Assets\SplashScreen.png"`

Windows will choose the right `scale-*` file automatically.

## Suggested manifest background color
Use a dark science-forward brand background:
- **#0F172A**

## Store listing upload
In Partner Center, upload:
- **Store_Listing/Verbinal_AppTileIcon_300x300.png** as the **1:1 App tile icon**

## Notes
- Transparent assets are used for app list / taskbar contexts.
- Plated blue assets are used for tiles, splash screens, and store-facing surfaces.
- Small icons were simplified so the mark stays readable at 16–32 px.
