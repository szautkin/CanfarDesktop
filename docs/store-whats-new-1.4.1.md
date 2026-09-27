# Store submission — "What's new in this version" (1.4.1.0)

Plain-text blocks ready to paste into Partner Center (one per listing language). Partner Center allows
1500 characters: English is 1333, French 1473.

## English (en-US)

```
Verbinal 1.4.1 adds cutouts — only the part of a file you need — and many fixes.
• Cutouts from the archive — CADC cuts the region on its side: a few MB of a 1.6 GB tile, at full resolution. Draw a circle or box on the file's footprint or type RA, Dec and size; for a cube, a wavelength range too.
• Cutouts on your computer — cut a file you have downloaded, at once and offline: HST and MegaPrime multi-extension files, cubes and fpack .fz included. Weight maps come along, cut to the same pixels.
• Search's Spatial cutout and Spectral cutout options now cut each download to the search's region and wavelengths.
• Copy an observation or any search result as text that pastes into a spreadsheet.
• Keep an observation in Research without its file, or remove the file and keep your notes.
• The Portal is laid out as in the Linux app, with the launch form in a dialog.
• Your AI assistant can open Settings to show you where something is set, and reconnects by itself when Verbinal restarts.
Also fixed:
• A long search can be cancelled, and "Public only" works again.
• Proprietary files download for their owner.
• Very large images (a 1.6 GB tile) open when there is memory for them.
• Positions on HST images with SIP distortion land on the right pixel.
• Marks come back when a file is reopened by another spelling of its path.
```

## French (fr-FR)

```
Verbinal 1.4.1 ajoute les découpes — juste la partie d'un fichier qu'il vous faut — et de nombreuses corrections.
• Découpes depuis l'archive — le CADC découpe la région chez lui : quelques Mo d'une tuile de 1,6 Go, en pleine résolution. Cercle ou rectangle tracé sur l'empreinte ou saisi en AD, Déc et taille ; pour un cube, une plage spectrale.
• Découpes sur votre ordinateur — d'un fichier déjà téléchargé, aussitôt et hors ligne, y compris HST et MegaPrime multi-extensions, cubes et fpack .fz. Les cartes de poids suivent, sur les mêmes pixels.
• Dans Rechercher, Découpe spatiale et Découpe spectrale découpent désormais chaque téléchargement.
• Copiez une observation ou tout résultat en texte à coller dans un tableur.
• Gardez une observation dans Recherche sans son fichier, ou supprimez le fichier et gardez vos notes.
• Le Portail reprend la disposition de la version Linux, avec le lancement dans une boîte de dialogue.
• Votre assistant IA peut ouvrir les Réglages pour vous guider, et se reconnecte seul quand Verbinal redémarre.
Corrigé aussi :
• Une longue recherche peut être annulée, et « Données publiques seulement » fonctionne de nouveau.
• Les fichiers propriétaires se téléchargent pour leur propriétaire.
• Les très grandes images (tuile de 1,6 Go) s'ouvrent si la mémoire le permet.
• Les positions sur les images HST à distorsion SIP tombent sur le bon pixel.
• Les marques reviennent quand un fichier est rouvert par un autre chemin équivalent.
```

## Submission notes (internal)

- Package version: **1.4.1.0** (Package.appxmanifest and CanfarDesktop.csproj `<Version>` in lockstep).
- Architectures: x86, x64 and ARM64 — one `.msixupload` each, as for 1.4.0.
- Capabilities unchanged: `runFullTrust` only — nothing new to justify in certification. No new network
  hosts: SODA cutouts go to the same CADC services as DataLink and downloads.
- The bridge changed in this release: it answers "not running" while Verbinal is closed and reconnects
  when it starts. The package build publishes it from the source being packaged, so a stale bridge
  means a stale build: delete `AppPackages\` and `bin\...\Upload\` and repackage.
- Check each Upload `.msix` has `mcp-bridge/CanfarDesktop.McpBridge.exe` for its own architecture (PE
  machine x86 `0x14C`, x64 `0x8664`, ARM64 `0xAA64`) and `AGENTS.md` at its root.
- Before submitting, install the x64 package and run `scripts/mcp-smoke.ps1` against it: first with
  Verbinal closed (the bridge must answer), then with it running and the MCP server on. It exits 0 when
  every check passes.
- `Properties/PublishProfiles/win-*.pubxml` are now in the repository. A Release build takes its
  RuntimeIdentifier from them; a checkout without them failed with NETSDK1094, which is what broke CI.
