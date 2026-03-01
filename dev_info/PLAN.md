# CANFAR Desktop — Implementation Plan

## Overview

Native WinUI 3 desktop client for the CANFAR Science Portal, porting the Next.js/React web application to a C# Windows desktop app. Focus: **CANFAR authentication mode** first.

**Source reference:** `~/source/repos/science-portal-next`

---

## Architecture

### Technology Stack

| Layer | Technology |
|---|---|
| UI Framework | WinUI 3 (Windows App SDK 1.8+) |
| Target | .NET 8.0, Windows 10 1809+ |
| Architecture Pattern | MVVM (Model-View-ViewModel) |
| HTTP Client | `HttpClient` via `IHttpClientFactory` |
| Dependency Injection | `Microsoft.Extensions.DependencyInjection` |
| JSON | `System.Text.Json` |
| Settings/Preferences | `Windows.Storage.ApplicationData` / local settings |
| Charts | WinUI `ProgressBar` or community toolkit charts |
| Navigation | Single-page dashboard (no frame navigation needed) |

### Project Structure

```
CanfarDesktop/
├── App.xaml / App.xaml.cs              # App entry, DI container setup
├── MainWindow.xaml / .cs               # Shell: TitleBar + NavigationView + Content
│
├── Models/                             # Plain data models (DTOs)
│   ├── Session.cs                      # Session, SkahaSessionResponse
│   ├── SessionLaunchParams.cs          # Launch request parameters
│   ├── ContainerImage.cs               # RawImage, ParsedImage, ImagesByType
│   ├── SessionContext.cs               # CPU/RAM/GPU options from Skaha
│   ├── PlatformLoad.cs                 # CPU/RAM/instance stats
│   ├── UserInfo.cs                     # User profile data
│   ├── StorageQuota.cs                 # VOSpace quota data
│   └── AuthResult.cs                   # Login response (token + user)
│
├── Services/                           # API clients & business logic
│   ├── IAuthService.cs / AuthService.cs
│   ├── ISessionService.cs / SessionService.cs
│   ├── IImageService.cs / ImageService.cs
│   ├── IPlatformService.cs / PlatformService.cs
│   ├── IStorageService.cs / StorageService.cs
│   ├── ISettingsService.cs / SettingsService.cs
│   └── HttpClients/
│       └── SkahaHttpClient.cs          # Configured HttpClient with base URL + auth header
│
├── ViewModels/                         # MVVM ViewModels
│   ├── MainViewModel.cs                # Shell state, auth status, navigation
│   ├── SessionListViewModel.cs         # Active sessions list
│   ├── SessionLaunchViewModel.cs       # Launch form logic
│   ├── PlatformLoadViewModel.cs        # Platform stats
│   ├── StorageViewModel.cs             # Storage quota
│   ├── LoginViewModel.cs               # Login form
│   └── Base/
│       └── ObservableViewModel.cs      # Base class (INotifyPropertyChanged)
│
├── Views/                              # XAML pages / controls
│   ├── DashboardPage.xaml / .cs        # Main dashboard layout (all widgets)
│   ├── Controls/
│   │   ├── SessionCard.xaml / .cs      # Individual session card
│   │   ├── SessionListControl.xaml / .cs
│   │   ├── LaunchFormControl.xaml / .cs
│   │   ├── PlatformLoadControl.xaml / .cs
│   │   ├── StorageQuotaControl.xaml / .cs
│   │   └── MetricBar.xaml / .cs        # Reusable metric bar (CPU/RAM/etc.)
│   └── Dialogs/
│       ├── LoginDialog.xaml / .cs
│       ├── DeleteSessionDialog.xaml / .cs
│       ├── RenewSessionDialog.xaml / .cs
│       ├── SessionEventsDialog.xaml / .cs
│       └── LaunchStatusDialog.xaml / .cs
│
├── Converters/                         # XAML value converters
│   ├── StatusToColorConverter.cs
│   ├── BoolToVisibilityConverter.cs
│   └── BytesToGBConverter.cs
│
├── Helpers/
│   ├── ImageParser.cs                  # Port of image-parser.ts
│   ├── TokenStorage.cs                 # Secure token persistence (CredentialManager or DPAPI)
│   └── ApiEndpoints.cs                 # API URL constants
│
└── Assets/                             # Icons, images (existing)
```

### Key Architecture Decisions

1. **Direct API calls** — No intermediate proxy layer needed. The Next.js app uses API routes as a proxy to add auth headers and avoid CORS. A desktop app can call Skaha/CADC APIs directly with `HttpClient`.

2. **MVVM without framework** — Use `CommunityToolkit.Mvvm` (source generators for `ObservableProperty`, `RelayCommand`) to keep ViewModels clean without a heavy framework.

3. **Single-page dashboard** — The app is one screen with 4 panels (sessions, launch form, platform load, storage). No multi-page navigation needed. Use a `Grid` layout matching the web app.

4. **Token storage** — Use `Windows.Security.Credentials.PasswordVault` for secure token persistence (replaces localStorage in the web app).

5. **No TanStack Query equivalent** — Implement simple service-level caching with manual refresh. C# `HttpClient` + async/await + `ObservableCollection` covers the data flow.

---

## External APIs (Direct from Desktop)

The desktop app calls these APIs directly (no Next.js proxy):

| API | Base URL | Auth | Purpose |
|---|---|---|---|
| **CADC Login** | `https://ws-uv.canfar.net/cred/auth/priv/login` | HTTP Basic | Get bearer token |
| **CADC WhoAmI** | `https://ws-uv.canfar.net/cred/auth/priv/whoami` | Bearer | Verify token |
| **CADC AC** | `https://ws-uv.canfar.net/ac` | Bearer | User details (`/users/{username}`) |
| **Skaha Sessions** | `https://ws-uv.canfar.net/skaha/v1/session` | Bearer | CRUD sessions |
| **Skaha Images** | `https://ws-uv.canfar.net/skaha/v1/image` | Bearer | List container images |
| **Skaha Context** | `https://ws-uv.canfar.net/skaha/v1/context` | Bearer | CPU/RAM/GPU options |
| **Skaha Stats** | `https://ws-uv.canfar.net/skaha/v1/stats` | Bearer | Platform load |
| **VOSpace/Cavern** | `https://ws-uv.canfar.net/cavern/nodes/home/{user}` | Bearer | Storage quota (XML) |

> Base URLs should be configurable via app settings for different deployments.

---

## Implementation Phases

### Phase 1: Foundation + Auth (Epic 1)
- Project setup: NuGet packages, DI, folder structure
- Models, services interfaces, base ViewModel
- CANFAR login flow (username/password → token)
- Secure token storage
- MainWindow shell with TitleBar and auth state display

### Phase 2: Session List (Epic 2)
- SessionService: fetch sessions from Skaha
- Session model mapping (SkahaResponse → Session)
- SessionCard control (type icon, name, status, times, resources)
- SessionListControl with refresh
- Open session in browser (connectURL)

### Phase 3: Session Management (Epic 3)
- Delete session with confirmation dialog
- Renew session (extend expiry) dialog
- View session events/logs dialog
- Status indicators (Running/Pending/Failed/Terminating)

### Phase 4: Session Launch (Epic 4)
- ImageService: fetch images + context (CPU/RAM/GPU options)
- Image parser (registry/project/name/version grouping)
- LaunchFormControl: type → project → image → name → resources
- Session launch API call + status dialog
- Post-launch polling until Running/Failed

### Phase 5: Platform Load + Storage (Epic 5)
- PlatformService: fetch stats
- PlatformLoadControl: 3 metric bars (CPU, RAM, instances)
- StorageService: fetch VOSpace XML, parse quota
- StorageQuotaControl: used/quota/percentage

### Phase 6: Polish (Epic 6)
- Light/Dark theme support (WinUI system theme)
- User preferences persistence
- Error handling & retry UX
- Loading skeletons / progress indicators
- Keyboard accessibility

---

## NuGet Packages

| Package | Purpose |
|---|---|
| `CommunityToolkit.Mvvm` | MVVM source generators (ObservableProperty, RelayCommand) |
| `CommunityToolkit.WinUI.Controls` | Additional WinUI controls (SettingsCard, etc.) |
| `Microsoft.Extensions.DependencyInjection` | IoC container |
| `Microsoft.Extensions.Http` | IHttpClientFactory |
| `System.Text.Json` | JSON serialization (built-in) |

---

## Configuration

App settings (stored in local app data):

```json
{
  "ApiBaseUrl": "https://ws-uv.canfar.net",
  "SkahaApiPath": "/skaha/v1",
  "LoginApiPath": "/cred/auth/priv",
  "AcApiPath": "/ac",
  "StorageApiPath": "/cavern/nodes/home",
  "Theme": "System",
  "DefaultSessionType": "notebook",
  "DefaultCores": 2,
  "DefaultRam": 8
}
```
