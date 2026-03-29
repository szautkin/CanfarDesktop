# Verbinal for Windows

A native Windows desktop companion for the [CANFAR Science Portal](https://www.canfar.net/), built with C#, WinUI 3, and the Windows App SDK.

This is the Windows counterpart of [Verbinal for macOS](https://github.com/szautkin/canfar-macos) (SwiftUI), [Verbinal for Linux](https://github.com/szautkin/CanfarDesktopUbuntu) (Rust/GTK 4), and [Verbinal for Android](https://github.com/szautkin/canfar-android) (Kotlin/Jetpack Compose).

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

## Features

- **Session Management** — Launch, monitor, extend, and delete CANFAR science sessions (Notebook, Desktop, CARTA, Contributed, Firefly)
- **Session Details** — View session events and container logs in a dialog
- **Storage Quota** — View VOSpace home directory usage at a glance
- **Platform Load** — Real-time cluster CPU, GPU, and RAM utilisation
- **Recent Launches** — Quick re-launch from session history
- **Standard & Advanced Launch** — Pick from the CANFAR image catalogue or supply a custom registry image with auth credentials
- **Auto-Refresh** — Active sessions poll automatically while any session is pending
- **Secure Credentials** — Tokens stored in Windows Credential Manager with optional "Remember me"

## Screenshot

*(coming soon)*

## Installation

### Microsoft Store

Install directly from the [Microsoft Store](https://apps.microsoft.com/detail/9p8jqvk4pjch?ocid=webpdpshare).

### Build from source

See [Building](#building) below.

## Requirements

### Runtime
- Windows 10 (1809) or newer
- A CANFAR account

### Build
- Visual Studio 2022 17.8+ with the **.NET desktop development** and **Windows application development** workloads
- .NET 8 SDK
- Windows App SDK 1.8+

## Building

```powershell
# Clone the repository
git clone https://github.com/szautkin/CanfarDesktop.git
cd CanfarDesktop

# Restore and build
dotnet restore
dotnet build -c Debug

# Or open CanfarDesktop.slnx in Visual Studio and run the Verbinal project
```

## Running Tests

```powershell
dotnet test CanfarDesktop.Tests
```

## Code Quality

- MVVM architecture with CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection for service registration
- Unit tests with xUnit and NSubstitute
- Strict separation of concerns: Views, ViewModels, Services, and Models
- Nullable reference types enabled

## Project Structure

```
CanfarDesktop.slnx         # Solution file
CanfarDesktop.csproj       # Main application project
Assets/                    # App icons, session type images
Converters/                # XAML value converters
Helpers/                   # API endpoints, token storage, image parsing
Models/                    # Data classes (Session, Image, PlatformLoad, etc.)
Services/                  # API clients and business logic
ViewModels/                # MVVM view models
Views/
  Controls/                # Reusable XAML controls (session card, launch form, etc.)
  Dialogs/                 # Login, delete confirmation, session events dialogs
  DashboardPage.xaml       # Main dashboard view
CanfarDesktop.Tests/       # Unit tests (xUnit + NSubstitute)
```

## Tech Stack

- **Language:** C# 12 / .NET 8
- **UI:** WinUI 3 (Windows App SDK 1.8)
- **Architecture:** MVVM with CommunityToolkit.Mvvm
- **DI:** Microsoft.Extensions.DependencyInjection
- **Networking:** HttpClient with typed handlers
- **Testing:** xUnit + NSubstitute
- **Packaging:** MSIX (Microsoft Store)

## API Endpoints

All communication is with CANFAR services over HTTPS. No telemetry, analytics, or third-party calls.

| Service | Base URL | Purpose |
|---------|----------|---------|
| Auth | `ws-cadc.canfar.net/ac` | Login, token validation, user info |
| Sessions | `ws-uv.canfar.net/skaha/v1` | Session CRUD, images, context, stats |
| Storage | `ws-uv.canfar.net/arc` | VOSpace quota |

## License

[GNU Affero General Public License v3.0](LICENSE)

Copyright (C) 2025 Serhii Zautkin

## Privacy

See [PRIVACY.md](PRIVACY.md). In short: no data collection, no telemetry, no third-party services. All data stays on your machine or goes directly to CANFAR.
