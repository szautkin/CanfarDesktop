# Microsoft Store — Privacy / Data-Collection Declaration

This document captures, in-repo, the answers Verbinal provides for the Microsoft
Store (Partner Center) **Privacy** and **Data collection** sections, mirroring the
macOS App Privacy posture. It is the single source of truth for what the app
collects (nothing) and why the declared capabilities are minimal.

See also [PRIVACY.md](../PRIVACY.md) (human-readable privacy notice) and the
in-app **Terms of Use** (`Helpers/LegalTerms.cs`).

## Summary

- **No data collected.** Verbinal contains **no analytics, telemetry, advertising,
  or third-party SDKs**. It makes no network calls to any service other than
  CANFAR/CADC.
- **No tracking.** The app does not track users across apps or websites and does
  not build user profiles.

## Data types

| Data type | Collected? | Linked to user? | Used for tracking? | Notes |
|-----------|------------|-----------------|--------------------|-------|
| Account credentials (CANFAR username/password, session token) | **Not collected by the developer** | n/a | No | Stored **only on the device** in Windows Credential Manager (PasswordVault). Used solely to authenticate to CANFAR/CADC. Never transmitted anywhere except trusted CANFAR/CADC hosts over HTTPS. |
| Files & FITS data the user opens/downloads | **Not collected by the developer** | n/a | No | Read/written on the local device or the user's own VOSpace at the user's request. |
| Diagnostics / crash logs | **Not collected by the developer** | n/a | No | A local, token-scrubbed `crash.log` is written under the app's local folder for the user's own troubleshooting (`Helpers/CrashLogger.cs`). Not transmitted. Aggregate crash analytics, if any, come from the Store/Partner Center platform, not from app code. |

## Network

All app-initiated traffic is HTTPS and goes directly to trusted CANFAR/CADC hosts.
The session bearer token is attached **only** to requests for hosts on an explicit
allowlist (`Helpers/TrustedHosts.cs`): `*.canfar.net` and
`*.cadc-ccda.hia-iha.nrc-cnrc.gc.ca`. DataLink results that are not HTTPS are
rejected, and the token is not forwarded across cross-host redirects.

## Capabilities (least privilege)

`Package.appxmanifest` declares only:

- `runFullTrust` — required for the local Python kernel used by the Notebook
  module (process launch / local file access).

No `broadFileSystemAccess`, no background tasks, no location, no microphone/camera,
and no other restricted capabilities are requested.

## Credentials & secrets

- No secrets, keys, or credentials are present in source.
- Credentials live only in Windows Credential Manager (device-only) and are never
  written to logs (see `Helpers/LogScrubber.cs`).
