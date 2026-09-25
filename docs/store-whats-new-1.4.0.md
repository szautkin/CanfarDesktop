# Store submission — "What's new in this version" (1.4.0.0)

Plain-text blocks ready to paste into Partner Center (one per listing language). Partner Center allows
1500 characters: English is 1361, French 1490.

## English (en-US)

```
Verbinal 1.4.0 adds marks, figure export and Remote Compute, and your AI assistant can now see what you see.
• Marks — draw boxes, circles, callouts and text on FITS images and cubes, pinned to the sky; style them, filter them, and export them as JSON or DS9 regions.
• Figure export — PNG or PDF of the view or a selected region, up to 4×, with your marks, a header and a footer.
• Remote Compute — a new screen for the code your AI assistant runs on CANFAR: the session's status with Start and Stop, every run with its code and output, and a box to run Python or Bash yourself.
• AI assistant — it can look at the viewers, point at controls on screen, and give you a guided tour of the app. Codex, Copilot, Cursor, Gemini and other MCP clients can connect too.
• Container images — search the registry for images the platform doesn't list, and see what's installed inside.
• ADQL is checked against the service's schema as you type.
• Notebooks open .py and .md files, and outputs render SVG, Markdown, LaTeX and HTML.
• The home screen is reordered; Portal, Remote Compute and Storage open once you sign in.
Also fixed:
• Positions on images with rotated coordinates (e.g. JWST) no longer drift.
• Empty or failed downloads are no longer recorded as successful.
• The assistant connection now works on 32-bit Windows.
• Service health shows planned downtime.
```

## French (fr-FR)

```
Verbinal 1.4.0 ajoute les marques, l'export de figures et le calcul distant, et votre assistant IA voit désormais ce que vous voyez.
• Marques — dessinez rectangles, cercles, bulles et texte sur les images FITS et les cubes, ancrés au ciel, à styler, filtrer et exporter en JSON ou en régions DS9.
• Export de figures — PNG ou PDF de la vue ou d'une zone choisie, jusqu'à 4×, avec marques, en-tête et pied de page.
• Calcul distant — un nouvel écran pour le code que votre assistant exécute sur CANFAR : la session avec Démarrer et Arrêter, chaque exécution avec son code et sa sortie, et une zone pour lancer vous-même du Python ou du Bash.
• Assistant IA — il peut regarder les visionneuses, pointer les commandes à l'écran et vous faire visiter l'application. Codex, Copilot, Cursor, Gemini et d'autres clients MCP peuvent aussi se connecter.
• Images de conteneur — cherchez dans le registre les images absentes de la plateforme, et voyez leur contenu.
• L'ADQL est vérifié selon le schéma du service à la saisie.
• Les calepins ouvrent les .py et .md ; les sorties affichent SVG, Markdown, LaTeX et HTML.
• L'accueil est réorganisé ; Portail, Calcul distant et Stockage s'ouvrent une fois connecté.
Corrigé aussi :
• Les positions sur les images aux coordonnées pivotées (ex. JWST) ne dérivent plus.
• Un téléchargement vide ou échoué n'est plus enregistré comme réussi.
• La connexion de l'assistant fonctionne sous Windows 32 bits.
• L'état des services signale les arrêts planifiés.
```

## Submission notes (internal)

- Package version: **1.4.0.0** (Package.appxmanifest and CanfarDesktop.csproj `<Version>` in lockstep).
- Architectures: x86, x64 and, new in this release, **ARM64** — one `.msixupload` each.
- Capabilities unchanged: `runFullTrust` only — nothing new to justify in certification.
- The MCP bridge is no longer a manual step: every Release package build publishes it for its own
  architecture and fails if it is missing. Package from **9f5c040 or later** — before it, a connected
  assistant kept the old bridge after an update, and the wizard could register the install folder.
- Check each Upload `.msix` has `mcp-bridge/CanfarDesktop.McpBridge.exe` for its own architecture (PE
  machine x86 `0x14C`, x64 `0x8664`, ARM64 `0xAA64`) and `AGENTS.md` at its root. Sizes for reference:
  `.msixupload` x86 ≈ 88 MB, x64 ≈ 114 MB, ARM64 ≈ 110 MB.
- If the payload looks stale, delete `AppPackages\` and `bin\...\Upload\` and repackage.
