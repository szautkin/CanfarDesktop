# Store submission — "What's new in this version" (1.3.2.0)

Plain-text blocks ready to paste into Partner Center (one per listing language).

## English (en-US)

```
Verbinal 1.3.2 is a reliability update for Store installs.

• Notebook: fixed the Python kernel failing to start ("file not found") on packaged installs, and made rapid interrupt/restart safe
• AI assistant: connecting Claude Desktop now works reliably — the connection helper is kept in a stable location that survives app updates, and when Verbinal can't edit Claude's config file directly it now guides you through a quick copy-paste instead of failing silently
• Landing page: scrolls when the window is short and reflows the tiles when it's narrow, so no tile is ever out of reach
• Updated to Windows App SDK 2.2 with refreshed runtime components
```

## French (fr-FR)

```
Verbinal 1.3.2 est une mise à jour de fiabilité pour les installations du Store.

• Notebook : correction du noyau Python qui ne démarrait pas (« fichier introuvable ») dans les installations empaquetées, et interruption/redémarrage rapides désormais sûrs
• Assistant IA : la connexion à Claude Desktop fonctionne désormais de manière fiable — l'outil de connexion est conservé à un emplacement stable qui survit aux mises à jour, et lorsque Verbinal ne peut pas modifier directement le fichier de configuration de Claude, il vous guide à travers un simple copier-coller au lieu d'échouer silencieusement
• Page d'accueil : défilement vertical quand la fenêtre est basse et réorganisation des tuiles quand elle est étroite — aucune tuile n'est jamais hors de portée
• Passage au Windows App SDK 2.2 avec des composants d'exécution actualisés
```

## Submission notes (internal)

- Package version: **1.3.2.0** (Package.appxmanifest already bumped).
- Capabilities unchanged: `runFullTrust` only — no new restricted capabilities, nothing new to justify in certification.
- Privacy declaration unchanged: no new data collection in this release (see store-privacy-declaration.md).
- Before packaging, stage the MCP bridge or the Store build ships without it:
  `dotnet publish CanfarDesktop.McpBridge -c Release -r win-x64 /p:SelfContained=true /p:PublishSingleFile=true /p:Platform=x64 -o mcp-bridge`
  Then verify the Upload .msix contains `mcp-bridge/CanfarDesktop.McpBridge.exe` (~107 MB package; ~78 MB means the bridge is missing).
- If the payload looks stale, delete `AppPackages\` and `bin\...\Upload\` and repackage.
