# Storage

A local-first mobile inventory app that helps you photograph, tag, and organize your belongings across any storage space - from cellars to closets.

## Overview

Storage helps you catalog and find items in your home by combining photos, manual tagging, and location-based organization. Everything works offline - no cloud dependencies.

## Tech Stack

- **.NET MAUI** - Cross-platform mobile framework (Android-first, iOS second)
- **SQLite + EF Core** - Local database for offline-first storage
- **CommunityToolkit.Maui.Camera** - In-app camera. This is the only toolkit package referenced; the main `CommunityToolkit.Maui` package is not used
- **ML.NET / ONNX Runtime** - On-device AI for image recognition (post-MVP)
- **C# 12+** - Modern language features (records, pattern matching, file-scoped namespaces)

## Architecture

- **MVVM pattern** - Standard MAUI architecture
- **Repository pattern** - Data access layer abstraction
- **Dependency injection** - Built-in MAUI container
- **Local-first** - All data stored on device, no cloud sync

## Features (Delivered)

- 🏷️ Tags as first-class entities - many-to-many, autocomplete on the item form, a Tags tab for managing them, and a colour per tag
- 🗂️ Location organization, flat or nested
- ✏️ Add/edit items and locations, with quantity stepper
- 📋 Read-only item detail view, with the photo at full width and Edit/Delete on the page
- 📸 Photo capture and attachment to items — **Android only for now**; iOS is deferred
- 🖼️ Multiple photos per item, with a chosen primary

## Features (In Progress / Planned)

- 🤖 AI-powered category suggestion from photos (Phase 4)
- 🔍 Search by name, description, or tags (Phase 5)
- 💾 Export/import for data backup (Phase 6)

## Development Phases

The phase roadmap and current build status both live in [Linear](https://linear.app/melnyk/project/storage-25af39369510) — phases are defined there, not in this repo.

Current status: **Phases 1 (MVP) and 3 (Photo Capture) complete.** Phase 5 is partly done —
tags are delivered, search and polish are outstanding. Phase 2 (Design & Visual Identity) is
next; VOL-32 was pulled forward from it. Linear has the per-issue detail.

### Photo capture is Android-only

The in-app camera uses `CommunityToolkit.Maui.Camera`, and only the Android side has been
wired up and tested: the `CAMERA` permission is declared in the Android manifest, and no
`Info.plist` usage strings or iOS-specific handling have been added. Photo capture will not
work on an iOS build until that is done.

## Getting Started

### Prerequisites

- .NET 10 SDK
- The MAUI workload (`dotnet workload install maui`)
- Visual Studio 2022 or JetBrains Rider
- Android SDK (for Android development)
- Xcode (for iOS development, macOS only)

### Running the App

```bash
# Clone the repository
git clone <repository-url>
cd Storage

# Restore dependencies
dotnet restore

# Run on Android
dotnet build src/Storage.App/Storage.App.csproj -t:Run -f net10.0-android

# Run on iOS (macOS only)
dotnet build src/Storage.App/Storage.App.csproj -t:Run -f net10.0-ios

# Run the tests
dotnet test tests/Storage.Tests/Storage.Tests.csproj
```

The app also declares `net10.0-maccatalyst`, and `net10.0-windows10.0.19041.0` when building on Windows.

### A note on the pinned MAUI version

`Storage.App.csproj` pins `<MauiVersion>10.0.60</MauiVersion>`. The camera package needs
Microsoft.Maui.Controls 10.0.60 or newer, while the newest MAUI workload manifest still
pins 10.0.20 — without the pin, the restore fails with `NU1605`. MAUI 10.0.x services
through NuGet ahead of the workload manifest, so this is the supported way to consume it
rather than a workaround. The csproj comment on the property has the full explanation.

## Project Structure

```
Storage/
├── src/
│   ├── Storage.App/       # MAUI app - UI, ViewModels, platform-bound services
│   │   ├── Controls/      # Custom controls
│   │   ├── Converters/    # XAML value converters
│   │   ├── Platforms/     # Android, iOS, MacCatalyst, Windows heads
│   │   ├── Properties/    # Launch settings
│   │   ├── Resources/     # Images, fonts, styles, app icon, splash
│   │   ├── Services/      # Platform implementations of the Core interfaces
│   │   ├── ViewModels/    # MVVM view models
│   │   └── Views/         # MAUI pages
│   └── Storage.Core/      # Platform-agnostic domain - no MAUI dependency
│       ├── Data/          # EF Core DbContext
│       ├── Migrations/    # EF Core migrations
│       ├── Models/        # Item, ItemPhoto, Location, Tag
│       ├── Repositories/  # Repository-pattern data access
│       └── Services/      # PhotoService and its platform seams
└── tests/
    └── Storage.Tests/     # xUnit tests for Storage.Core
```

## Contributing

This is a personal learning project exploring mobile development. Feel free to fork and experiment.

## License

TBD
