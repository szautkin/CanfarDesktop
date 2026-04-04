---
name: Stakeholder Review Q2 2026
description: Comprehensive product review of Verbinal v1.0.6 -- feature completeness, release readiness, storage gaps, and priority recommendations
type: project
---

Full stakeholder review conducted 2026-04-01 on Verbinal v1.0.6.0.

**Three pillars assessed:** Portal (session management), Search (CADC TAP archive), Research (local observation library).

Key findings:
- Portal is production-ready: sessions, launch (standard+advanced), batch jobs with notifications, storage quota, platform load, recent launches, session events/renew/delete.
- Search is the strongest feature: 4-column constraint form matching CADC web, ADQL editor, data train, per-column filtering, sort, pagination, download, preview, CSV/TSV export, recent searches, saved queries.
- Research is functional but minimal: local file list, preview images, metadata, open/show-in-explorer/delete. No batch operations.
- Storage is quota-only (read-only VOSpace property fetch). No file browsing, upload, or download.
- macOS source not accessible from Windows machine -- could not do direct comparison.
- App uses Mica backdrop, custom title bar, MSIX packaging, Windows App SDK 1.8, .NET 8.

**Why:** Establishes baseline for prioritization decisions and Store release planning.

**How to apply:** Reference when evaluating new feature requests or sequencing work. Storage file browsing and keyboard shortcuts are the two biggest gaps for power users.
