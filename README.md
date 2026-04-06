# Verbinal for Windows

A native Windows desktop companion for the [CANFAR Science Portal](https://www.canfar.net/), built with C#, WinUI 3, and the Windows App SDK.

This is the Windows counterpart of [Verbinal for macOS](https://github.com/szautkin/canfar-macos) (SwiftUI), [Verbinal for Linux](https://github.com/szautkin/CanfarDesktopUbuntu) (Rust/GTK 4), and [Verbinal for Android](https://github.com/szautkin/canfar-android) (Kotlin/Jetpack Compose).

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

## Screenshots

![Landing Page](docs/images/landing.png)

## Features

### [Portal](docs/02-portal.md) — Session Management
Launch, monitor, and manage CANFAR science sessions (JupyterLab, CARTA, NoVNC). View platform load, batch jobs, and session events/logs.

### [Search](docs/03-search.md) — CADC Archive
Query the Canadian Astronomy Data Centre archive with form-based search, ADQL editor, cascading data train filters, and download with real-time progress.

### [Research](docs/04-research.md) — Downloaded Observations
Browse downloaded FITS files with metadata cards, preview images, and one-click routing to the FITS Viewer.

### [Storage](docs/05-storage.md) — VOSpace Browser
Browse and manage VOSpace cloud files. Upload via drag-and-drop, download, create folders, and open FITS files directly in the viewer.

### [Notebook](docs/06-notebook.md) — Jupiter
A native WinUI Jupyter notebook engine. Multi-tab, local Python execution, Jupyter keyboard shortcuts, magic commands, matplotlib inline, autosave with crash recovery.

### [FITS Viewer](docs/07-fits-viewer.md) — Astronomical Images
Native FITS image viewer with WCS coordinates, multiple stretch modes and colormaps, North Up orientation, multi-tab comparison with linked crosshair, sync zoom, and blink comparison.

### Cross-Module Integration
- **Search to FITS** — Download from archive, view in FITS Viewer, crosshair back to Search
- **Storage to FITS** — Right-click a .fits file in VOSpace, open directly in the viewer
- **File Browser** — Side panel with local file navigation, routes .fits/.ipynb to the correct module
- **Back Navigation** — Navigate between modules without losing context

## Installation

### Microsoft Store

Install directly from the [Microsoft Store](https://apps.microsoft.com/detail/9p8jqvk4pjch?ocid=webpdpshare).

### Build from source

```powershell
git clone https://github.com/szautkin/CanfarDesktop.git
cd CanfarDesktop
dotnet restore
dotnet build -c Debug
```

Or open `CanfarDesktop.slnx` in Visual Studio 2022 and run.

## Requirements

### Runtime
- Windows 10 (1809) or newer
- A CANFAR account (for Portal and Storage)
- Python 3.8+ (for Notebook execution)

### Build
- Visual Studio 2022 17.8+ with **.NET desktop development** and **Windows application development** workloads
- .NET 8 SDK
- Windows App SDK 1.8+

## Running Tests

```powershell
dotnet test CanfarDesktop.Tests
```

529 tests covering: FITS parser, WCS coordinate transforms, viewport math, blink alignment, notebook parser, dirty tracking, autosave, recovery, ADQL builder, data train, VOTable parsing, and more.

## Architecture

- **Language:** C# 12 / .NET 8
- **UI:** WinUI 3 (Windows App SDK 1.8) with Mica backdrop
- **Architecture:** MVVM with CommunityToolkit.Mvvm
- **DI:** Microsoft.Extensions.DependencyInjection (44 registrations, 18 interfaces)
- **Networking:** HttpClient with typed handlers, all HTTPS
- **Testing:** xUnit + NSubstitute
- **Packaging:** MSIX (Microsoft Store)
- **Security:** Windows PasswordVault for credentials, no telemetry

## Project Structure

```
CanfarDesktop.slnx            Solution file
CanfarDesktop.csproj           Main application project
Models/                        Data classes
  Fits/                        FITS image models (WcsInfo, FitsHeader, WorldCoordinate)
  Notebook/                    Jupyter notebook document model
Services/                      API clients and business logic
  Fits/                        FITS parser, renderer, coordinate store
  Notebook/                    Kernel service, autosave, recovery
  HttpClients/                 Auth token handling
Helpers/                       Pure utility functions
  Notebook/                    Notebook parser, ANSI, syntax highlighting
  ViewportMath.cs              Testable coordinate transforms
  BlinkAligner.cs              Blink comparison alignment math
ViewModels/                    MVVM ViewModels
  Notebook/                    Notebook tab host, cell VMs
Views/
  FitsViewer/                  FITS viewer pages + tab host
  Notebook/                    Notebook pages + tab host
  Controls/                    Reusable controls (session card, etc.)
  Dialogs/                     Login, delete, session events
docs/                          Feature documentation with screenshots
CanfarDesktop.Tests/           Unit tests (xUnit + NSubstitute)
```

## License

[GNU Affero General Public License v3.0](LICENSE)

Copyright (C) 2025-2026 Serhii Zautkin

## Privacy

See [PRIVACY.md](PRIVACY.md). No data collection, no telemetry, no third-party services. All data stays on your machine or goes directly to CANFAR.
