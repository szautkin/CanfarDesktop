# Privacy Policy — Verbinal

**Effective date:** 1 March 2025
**Last updated:** 22 September 2026 (Verbinal 1.4.0)
**App name:** Verbinal — A CANFAR Science Portal Companion
**Publisher:** CodeBG (Serhii Zautkin)

---

## Summary

Verbinal does not collect, transmit, or sell any personal data to the developer,
to CodeBG, or to any third party. All data stays on your device or is sent
directly to the astronomy services you use from the app — CANFAR/CADC, and the
VizieR catalogue service when you run a catalogue search.

---

## 1. Data we collect

**None.** Verbinal does not have its own backend, analytics, telemetry,
crash-reporting service, or advertising SDK. The app contains no tracking code
of any kind.

## 2. Data stored on your device

Verbinal stores the following information locally, in the app's private
sandboxed container (Windows ApplicationData). No other application can access
this data.

| Data | Location | Purpose |
|---|---|---|
| CANFAR authentication token | Windows Credential Manager (PasswordVault) | Keeps you signed in between sessions when "Remember me" is checked |
| CANFAR username | Windows Credential Manager (PasswordVault) | Identifies the account associated with the saved token |
| Recent session launches | `recent_launches.json` in app local data | Shows your recent session history for quick re-launch |
| User preferences | Windows ApplicationData LocalSettings | Remembers your preferred session type, resource defaults, and theme |
| Image-registry secret (optional) | Windows Credential Manager (PasswordVault) | Lets image search and inspection reach the container registry, if you enter one |
| Search history and saved queries | App local data | Recent searches and queries you chose to save |
| Research library | App local data | Metadata of observations you downloaded, and your notes on them |
| Marks (annotations) | `annotations.json` in app local data | Marks you or an AI assistant drew on FITS images and cubes, keyed by file path |
| Recently opened files | App local data | Paths of FITS images, cubes and notebooks you opened, for the viewers' "recent" lists |
| Finished batch jobs | `job_history.json` in app local data | The outcome of recent jobs, kept after CANFAR removes them |
| Images you added | `user_images.json` in app local data | Container images you added from the registry |
| Pending AI-assistant requests | `mcp_proposals.json` in app local data | Changes an AI assistant proposed that are waiting for your approval, so they survive a restart |
| Remote compute runs (optional) | `compute_runs.json` in app local data | The code sent to your remote compute session — by you or an AI assistant — with who sent it and its outcome, for the Remote Compute screen. The output stays in your CANFAR storage |
| Crash log | `crash.log` in app local data | Local troubleshooting only; authentication tokens are removed before writing. Never transmitted |

All locally stored data is deleted when you uninstall the app or when you log
out (which clears credentials from Windows Credential Manager).

## 3. Data sent over the network

Verbinal communicates with CANFAR services operated by the Canadian Astronomy
Data Centre (CADC) and the Digital Research Alliance of Canada, and — only when
you run a catalogue search — with the VizieR service. CANFAR/CADC connections use
HTTPS, and your authentication token is only ever sent to CANFAR/CADC hosts.

| Endpoint | Data sent | Purpose |
|---|---|---|
| `ws-cadc.canfar.net` | Username and password (at login) | Authentication |
| `ws-uv.canfar.net/skaha` | Authentication token (Bearer header) | Session management, image listing, platform stats |
| `ws-uv.canfar.net/ac` | Authentication token | User profile retrieval |
| `ws-uv.canfar.net/arc` | Authentication token | Storage quota retrieval |
| `ws.cadc-ccda.hia-iha.nrc-cnrc.gc.ca` | Search queries; authentication token when signed in | Archive search, data links, downloads, target name resolution |
| `images.canfar.net` | Image search terms; registry secret if you entered one | Container image search and inspection |
| `tapvizier.cds.unistra.fr`, `tapvizier.u-strasbg.fr` (CDS, France); `vizier.china-vo.org` (last-resort mirror, HTTP) | The sky position, radius and catalogue of a cone search — no credentials, no account information | VizieR catalogue searches |

**Remote compute (optional, off until you set it up).** When you set a compute image in
Settings ▸ AI compute, code you or an AI assistant runs is written to your own CANFAR storage
(`.verbinal/exec` in your home folder) and run in a session on your own CANFAR account named
`verbinal-compute`, which uses your resource allocation. Nothing goes anywhere else. The Remote
Compute screen shows every run, and stopping the session there deletes it.

The CANFAR/CADC service addresses can be changed in Settings; the table shows the defaults.
Verbinal does **not** contact any other servers. There are no analytics
endpoints, no ad networks, and no third-party SDKs that make network requests.

### AI assistant (optional)

If you connect an AI assistant (for example Claude Desktop) in the AI Assistant
area, Verbinal answers that assistant's requests over a local connection on your
computer. Whatever the assistant reads through Verbinal — search results, file
names, image data, notebook contents — is then handled by that assistant and its
provider under their own privacy terms. Verbinal sends nothing to the assistant's
provider itself, and nothing is shared until you connect an assistant.

Your credentials are sent only to the CANFAR authentication endpoint
(`ws-cadc.canfar.net/ac/login`) and are never stored in plain text on disk.
The password is held in memory only for the duration of the login request and
is discarded immediately after.

## 4. Third-party services

Verbinal contains no third-party SDKs. Its external communication is with the
CANFAR platform and, for catalogue searches, the VizieR service, as described
above. CANFAR's own privacy practices are governed by the
[CADC Terms of Use](https://www.cadc-ccda.hia-iha.nrc-cnrc.gc.ca/en/about.html).

## 5. Children's privacy

Verbinal does not knowingly collect information from children under 13.
The app requires a CANFAR account, which is issued to researchers and
students by the Canadian Astronomy Data Centre.

## 6. Your choices

- **Don't save credentials:** Uncheck "Remember me" at login. No token or
  username will be persisted to Windows Credential Manager.
- **Clear saved credentials:** Log out from the app. This removes all stored
  tokens from Windows Credential Manager.
- **Clear recent launches:** Use the clear button in the Recent Launches panel,
  or uninstall the app to delete all local data.
- **Clear other local data:** Marks can be cleared from the Marks panel, finished
  jobs from the batch jobs history, and search history from the Search page.
- **Uninstall:** Removing the app deletes all sandboxed local data
  (settings, history, marks and the other files listed above). Credential
  Manager entries are also removed when the app package is uninstalled.

## 7. Changes to this policy

If this policy changes, the updated version will be published in the
application's source repository and in the Microsoft Store listing.
The effective date at the top of this document will be updated accordingly.

## 8. Contact

If you have questions about this privacy policy:

- **GitHub:** [github.com/CodeBG/Verbinal](https://github.com/CodeBG/Verbinal) (open an issue)
- **Developer:** Serhii Zautkin

---

*This privacy policy applies to the Verbinal application distributed through
the Microsoft Store and via source code under the AGPL-3.0 license.*
