# CANFAR Desktop — User Stories

> Priority: P0 = must-have, P1 = important, P2 = nice-to-have
> Stories are grouped by Epic and ordered by implementation sequence.

---

## Epic 1: Foundation & Authentication

### US-1.1: Project Setup [P0]
**As a** developer
**I want** the project scaffolded with proper architecture
**So that** I can build features on a solid foundation

**Acceptance Criteria:**
- [ ] NuGet packages installed: `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Http`
- [ ] Folder structure created: Models, Services, ViewModels, Views, Converters, Helpers
- [ ] DI container configured in `App.xaml.cs` with service registration
- [ ] `IHttpClientFactory` registered with named client for Skaha API
- [ ] `ObservableViewModel` base class with `INotifyPropertyChanged` via toolkit
- [ ] `ApiEndpoints` helper class with configurable base URLs

### US-1.2: CANFAR Login [P0]
**As a** scientist
**I want** to log in with my CADC username and password
**So that** I can access my sessions and resources

**Acceptance Criteria:**
- [ ] `LoginDialog` (ContentDialog) with username, password fields, and "Remember me" checkbox
- [ ] `AuthService` sends POST to CADC login endpoint with form-urlencoded credentials
- [ ] On success: bearer token extracted from response, stored securely
- [ ] `UserInfo` fetched from `/ac/users/{username}` after login
- [ ] Error handling: invalid credentials, network errors, server errors shown in dialog
- [ ] Login button in TitleBar area triggers the dialog

**API:**
- `POST {LoginApi}/login` — body: `username=x&password=y` → response body: base64 token
- `GET {LoginApi}/whoami` — header: `Authorization: Bearer {token}` → response: username string

### US-1.3: Token Persistence [P0]
**As a** scientist
**I want** to stay logged in between app restarts
**So that** I don't have to log in every time

**Acceptance Criteria:**
- [ ] Token stored in `Windows.Security.Credentials.PasswordVault`
- [ ] On app launch: check for stored token, validate with `/whoami`
- [ ] If valid: auto-login silently, show username in TitleBar
- [ ] If expired/invalid: clear stored token, show Login button
- [ ] Logout clears token from vault

### US-1.4: Auth State in Shell [P0]
**As a** scientist
**I want** to see my login status in the app title bar
**So that** I know if I'm authenticated

**Acceptance Criteria:**
- [ ] `MainWindow` shell: custom TitleBar with app icon + title + auth area
- [ ] Unauthenticated: "Login" button on the right
- [ ] Authenticated: username displayed, click opens menu (Logout)
- [ ] Loading state: progress ring while checking auth on startup
- [ ] `MainViewModel` exposes `IsAuthenticated`, `Username`, `IsLoading`

---

## Epic 2: Session List

### US-2.1: Fetch Active Sessions [P0]
**As a** scientist
**I want** to see all my active compute sessions
**So that** I can monitor what's running

**Acceptance Criteria:**
- [ ] `SessionService.GetSessionsAsync()` calls `GET {SkahaApi}/session`
- [ ] Response mapped from `SkahaSessionResponse[]` to `Session[]` model
- [ ] `SessionListViewModel` exposes `ObservableCollection<Session>`
- [ ] Auto-fetches on login, manual refresh via button
- [ ] Loading state: skeleton/progress while fetching
- [ ] Empty state: message when no sessions exist

**API:**
- `GET {SkahaApi}/v1/session` — header: `Authorization: Bearer {token}` → JSON array of session objects

### US-2.2: Session Card Display [P0]
**As a** scientist
**I want** to see session details at a glance
**So that** I can quickly understand each session's state

**Acceptance Criteria:**
- [ ] `SessionCard` UserControl displays:
  - Session type icon (notebook, desktop, carta, contributed, firefly)
  - Session name
  - Status badge with color (Running=green, Pending=yellow, Failed=red, Terminating=gray)
  - Container image name (parsed: `project/name:version`)
  - Start time and expiry time (formatted with relative time)
  - CPU, RAM, GPU allocation
  - "FLEX" badge if `isFixedResources=false`
- [ ] Cards arranged in a responsive `ItemsRepeater` or `ListView`
- [ ] Running sessions: click card or "Open" button opens `connectURL` in default browser

### US-2.3: Refresh Sessions [P1]
**As a** scientist
**I want** to refresh the session list
**So that** I see up-to-date statuses

**Acceptance Criteria:**
- [ ] Refresh button in sessions panel header
- [ ] Shows loading indicator during refresh
- [ ] Updates existing items, adds new, removes deleted
- [ ] Keyboard shortcut: `F5` or `Ctrl+R`

---

## Epic 3: Session Management

### US-3.1: Delete Session [P0]
**As a** scientist
**I want** to terminate a running session
**So that** I can free up resources

**Acceptance Criteria:**
- [ ] Delete button (trash icon) on each session card
- [ ] Confirmation dialog: "Are you sure you want to delete session '{name}'?"
- [ ] `SessionService.DeleteSessionAsync(id)` calls `DELETE {SkahaApi}/v1/session/{id}`
- [ ] On success: remove session from list (verify with re-fetch after 3s)
- [ ] On error: show error message, keep session in list
- [ ] Loading overlay on card during deletion

### US-3.2: Renew Session [P1]
**As a** scientist
**I want** to extend a session's expiry time
**So that** my work isn't interrupted

**Acceptance Criteria:**
- [ ] Clock/renew button on each session card
- [ ] `RenewSessionDialog`: shows current expiry, confirm button
- [ ] `SessionService.RenewSessionAsync(id)` calls `POST {SkahaApi}/v1/session/{id}/renew`
- [ ] On success: update expiry time in session list
- [ ] On error: show error message

### US-3.3: View Session Events [P1]
**As a** scientist
**I want** to view Kubernetes events for a session
**So that** I can debug issues

**Acceptance Criteria:**
- [ ] Events button (flag icon) on each session card
- [ ] `SessionEventsDialog` fetches `GET {SkahaApi}/v1/session/{id}?view=events`
- [ ] Displays events in a table: Type, Reason, Message, Timestamp
- [ ] Tab/toggle to show raw text view
- [ ] Logs view: `GET {SkahaApi}/v1/session/{id}?view=logs`

### US-3.4: Session Status Indicators [P0]
**As a** scientist
**I want** clear visual indicators of session status
**So that** I can quickly identify issues

**Acceptance Criteria:**
- [ ] Status badge colors:
  - Running → Green (`#4caf50`)
  - Pending → Amber (`#ff9800`)
  - Failed/Error → Red (`#f44336`)
  - Terminating → Gray (`#9e9e9e`)
- [ ] Pending sessions show subtle pulse animation
- [ ] Failed sessions show warning icon

---

## Epic 4: Session Launch

### US-4.1: Fetch Container Images [P0]
**As a** scientist
**I want** to see available container images
**So that** I can choose what software to run

**Acceptance Criteria:**
- [ ] `ImageService.GetImagesAsync()` calls `GET {SkahaApi}/v1/image`
- [ ] `ImageParser` groups images by session type → project → image name/version
- [ ] Images cached for 5 minutes
- [ ] `ImageService.GetContextAsync()` fetches CPU/RAM/GPU options from `GET {SkahaApi}/v1/context`

**API:**
- `GET {SkahaApi}/v1/image` → `[{ "id": "images.canfar.net/project/name:tag", "types": ["notebook","desktop"] }]`
- `GET {SkahaApi}/v1/context` → `{ "cores": { "default": 2, "options": [1,2,4,8,16] }, "memoryGB": { "default": 8, "options": [1,2,4,8,16,32] }, "gpus": { "options": [0,1,2] } }`

### US-4.2: Launch Form [P0]
**As a** scientist
**I want** to configure and launch a new compute session
**So that** I can do my research

**Acceptance Criteria:**
- [ ] `LaunchFormControl` with fields:
  1. **Session Type** — ComboBox: notebook, desktop, carta, contributed, firefly (with icons)
  2. **Project** — ComboBox: filtered by selected type
  3. **Container Image** — ComboBox: filtered by type + project (show name:version)
  4. **Session Name** — TextBox: auto-generated default, user can edit
  5. **Resource Type** — RadioButtons: "Flexible (platform-managed)" / "Fixed"
  6. **Cores** — Slider or NumberBox (only if Fixed): from context options
  7. **RAM (GB)** — Slider or NumberBox (only if Fixed): from context options
  8. **GPUs** — NumberBox (only if Fixed): from context options
- [ ] Cascading selection: changing type resets project/image; changing project resets image
- [ ] "Launch" button enabled only when required fields are filled
- [ ] Form remembers last selections (user preferences)

### US-4.3: Launch Session [P0]
**As a** scientist
**I want** to submit a session launch request
**So that** my session starts running

**Acceptance Criteria:**
- [ ] `SessionService.LaunchSessionAsync(params)` calls `POST {SkahaApi}/v1/session`
- [ ] Request body: form-data with `name`, `image`, `cores`, `ram`, `gpus`, `type`
- [ ] `LaunchStatusDialog` shows progress: Requesting → Success / Error
- [ ] On success: add pending session to list immediately
- [ ] Poll session status every 30s until `Running` or `Failed`
- [ ] On error: show error message with details

**API:**
- `POST {SkahaApi}/v1/session` — form-data: `name=x&image=y&cores=2&ram=8&type=notebook` → response: session ID

### US-4.4: Advanced Launch (Custom Image) [P2]
**As a** scientist
**I want** to launch a session with a custom container image
**So that** I can use my own software

**Acceptance Criteria:**
- [ ] "Advanced" expander/section in launch form
- [ ] Custom image URI text field
- [ ] Optional registry credentials (username + secret)
- [ ] Repository host selector

---

## Epic 5: Platform Load & Storage

### US-5.1: Platform Load Display [P1]
**As a** scientist
**I want** to see current platform resource usage
**So that** I know if resources are available

**Acceptance Criteria:**
- [ ] `PlatformService.GetStatsAsync()` calls `GET {SkahaApi}/v1/stats`
- [ ] `PlatformLoadControl` displays 3 metric bars:
  - CPU Cores: used / available
  - RAM (GB): used / available
  - Instances: running / max
- [ ] Each bar shows percentage and absolute values
- [ ] "Last updated" timestamp
- [ ] Manual refresh button

**API:**
- `GET {SkahaApi}/v1/stats` → `{ "instances": {...}, "cores": {...}, "ram": {...} }`

### US-5.2: Storage Quota Display [P1]
**As a** scientist
**I want** to see my storage usage
**So that** I know how much space I have left

**Acceptance Criteria:**
- [ ] `StorageService.GetQuotaAsync(username)` calls VOSpace endpoint
- [ ] Parse XML response to extract quota, used, available
- [ ] `StorageQuotaControl` displays: Used, Quota, Usage percentage
- [ ] Warning color when usage > 90%
- [ ] Fetches on login, manual refresh

**API:**
- `GET {StorageApi}/home/{username}` — header: `Authorization: Bearer {token}`, `Accept: text/xml` → VOSpace XML with `vos:Property` nodes for quota/size

---

## Epic 6: Polish & UX

### US-6.1: Theme Support [P2]
**As a** scientist
**I want** the app to match my Windows theme
**So that** it looks consistent with my system

**Acceptance Criteria:**
- [ ] Follow system light/dark theme by default
- [ ] Manual override: Light / Dark / System in settings
- [ ] Mica backdrop for modern Windows look
- [ ] Persist preference in local settings

### US-6.2: User Preferences [P2]
**As a** scientist
**I want** the app to remember my preferences
**So that** I don't have to reconfigure every time

**Acceptance Criteria:**
- [ ] Persist: default session type, default cores/RAM, theme
- [ ] Settings page or flyout accessible from user menu
- [ ] API base URL configurable (for different CANFAR deployments)

### US-6.3: Error Handling [P0]
**As a** scientist
**I want** clear error messages when things go wrong
**So that** I can understand and fix issues

**Acceptance Criteria:**
- [ ] Network errors: "Unable to connect to CANFAR. Check your internet connection."
- [ ] Auth errors (401): auto-logout, prompt re-login
- [ ] Server errors (500): "Server error. Please try again later."
- [ ] Validation errors: inline field validation in forms
- [ ] InfoBar (WinUI) for transient notifications (success/error)

### US-6.4: Loading States [P1]
**As a** scientist
**I want** to see loading indicators
**So that** I know the app is working

**Acceptance Criteria:**
- [ ] ProgressBar at top of each panel while loading
- [ ] Skeleton-style placeholders for session cards while loading
- [ ] ProgressRing overlay on cards during operations (delete/renew)
- [ ] Disable interactive elements during loading

### US-6.5: Keyboard & Accessibility [P2]
**As a** scientist
**I want** full keyboard navigation
**So that** I can use the app efficiently

**Acceptance Criteria:**
- [ ] All controls reachable via Tab
- [ ] Enter to submit forms/dialogs
- [ ] Escape to close dialogs
- [ ] F5 to refresh
- [ ] Screen reader friendly (AutomationProperties)

---

## Story Map (Implementation Order)

```
Phase 1 (Foundation):  US-1.1 → US-1.2 → US-1.3 → US-1.4
Phase 2 (Sessions):    US-2.1 → US-2.2 → US-2.3 → US-3.4
Phase 3 (Management):  US-3.1 → US-3.2 → US-3.3
Phase 4 (Launch):      US-4.1 → US-4.2 → US-4.3 → US-4.4
Phase 5 (Monitoring):  US-5.1 → US-5.2
Phase 6 (Polish):      US-6.3 → US-6.4 → US-6.1 → US-6.2 → US-6.5
```

---

## Definition of Done (per story)

- [ ] Code compiles without warnings
- [ ] MVVM pattern followed (no business logic in code-behind)
- [ ] Services accessed through interfaces (DI)
- [ ] Error states handled
- [ ] Manual testing passes acceptance criteria
