# Store submission — "What's new in this version" (1.3.3.0)

Plain-text blocks ready to paste into Partner Center (one per listing language).

## English (en-US)

```
Verbinal 1.3.3 completes the AI assistant's control of the viewers and fixes several accuracy issues.

• AI assistant: the FITS and Cube viewers are now fully drivable — blink comparison, HDU/extension switching, crosshair placement, tab switching, the opacity-curve editor, on-screen spectrum panel, channel profile, figure-export styles, and recent cubes
• Cube viewer: spectra on masked (blanked) cubes no longer fail, coordinates are true native pixels, and channel labels on large downsampled cubes now show the correct spectral value
• Service health: a service answering 404 or a server error is no longer reported as healthy, and the Settings connection test shows the real HTTP status
• Reliability: assistant tool calls no longer hang when the app is busy (they fail fast with a clear message), the cube notebook template explains how to install a missing package instead of crashing, and workflow files keep their exact line endings when a step is checked off
```

## French (fr-FR)

```
Verbinal 1.3.3 complète le contrôle des visionneuses par l'assistant IA et corrige plusieurs problèmes de précision.

• Assistant IA : les visionneuses FITS et de cubes sont désormais entièrement pilotables — comparaison par clignotement, changement de HDU/extension, placement du réticule, changement d'onglet, éditeur de courbe d'opacité, panneau de spectre à l'écran, profil de canaux, styles d'export de figures et cubes récents
• Visionneuse de cubes : les spectres sur les cubes masqués n'échouent plus, les coordonnées sont de vrais pixels natifs, et les étiquettes de canaux des grands cubes sous-échantillonnés affichent la bonne valeur spectrale
• État des services : un service répondant 404 ou une erreur serveur n'est plus signalé comme sain, et le test de connexion des paramètres affiche le vrai statut HTTP
• Fiabilité : les appels d'outils de l'assistant ne se bloquent plus quand l'application est occupée (échec rapide avec un message clair), le modèle de calepin pour cubes explique comment installer un paquet manquant au lieu de planter, et les fichiers de flux de travail conservent leurs fins de ligne exactes quand une étape est cochée
```

## Submission notes (internal)

- Package version: **1.3.3.0** (Package.appxmanifest bumped; CanfarDesktop.csproj `<Version>` now kept in lockstep).
- Capabilities unchanged: `runFullTrust` only — no new restricted capabilities, nothing new to justify in certification.
- Privacy declaration unchanged: no new data collection in this release (see store-privacy-declaration.md).
- Before packaging, stage the MCP bridge or the Store build ships without it:
  `dotnet publish CanfarDesktop.McpBridge -c Release -r win-x64 /p:SelfContained=true /p:PublishSingleFile=true /p:Platform=x64 -o mcp-bridge`
  Then verify the Upload .msix contains `mcp-bridge/CanfarDesktop.McpBridge.exe` (~107 MB package; ~78 MB means the bridge is missing).
- If the payload looks stale, delete `AppPackages\` and `bin\...\Upload\` and repackage.
