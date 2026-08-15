# Storage

A local-first mobile inventory app that helps you photograph, tag, and organize your belongings across any storage space - from cellars to closets.

## Overview

Storage helps you catalog and find items in your home by combining photos, manual tagging, and location-based organization. Everything works offline - no cloud dependencies.

## Tech Stack

- **.NET MAUI** - Cross-platform mobile framework (Android-first, iOS second)
- **SQLite + EF Core** - Local database for offline-first storage
- **MAUI Community Toolkit** - Camera integration
- **ML.NET / ONNX Runtime** - On-device AI for image recognition (post-MVP)
- **C# 12+** - Modern language features (records, pattern matching, file-scoped namespaces)

## Architecture

- **MVVM pattern** - Standard MAUI architecture
- **Repository pattern** - Data access layer abstraction
- **Dependency injection** - Built-in MAUI container
- **Local-first** - All data stored on device, no cloud sync

## Features (MVP)

- 🏷️ Free-text tags and descriptions
- 🗂️ Location organization, flat or nested
- 🔍 Search by tag/description
- ✏️ Add/edit items and locations, with quantity stepper
- 📸 Photo capture and attachment to items — **Android only for now**; iOS is deferred

## Features (In Progress / Planned)

- 🤖 AI-powered category suggestion from photos (Phase 4)
- 🖼️ Multiple photos per item
- 💾 Export/import for data backup

## Development Phases

See [PHASES.md](PHASES.md) for detailed implementation roadmap and [IMPLEMENTATION.md](IMPLEMENTATION.md) for current build status.

Current status: **Phase 1 (MVP) and Phase 3 (Photo Capture) complete** — Phase 2 (Design & Visual Identity) next.

### Photo capture is Android-only

The in-app camera uses `CommunityToolkit.Maui.Camera`, and only the Android side has been
wired up and tested: the `CAMERA` permission is declared in the Android manifest, and no
`Info.plist` usage strings or iOS-specific handling have been added. Photo capture will not
work on an iOS build until that is done.

## Getting Started

### Prerequisites

- .NET 8 SDK or later
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
dotnet build -t:Run -f net8.0-android

# Run on iOS (macOS only)
dotnet build -t:Run -f net8.0-ios
```

## Project Structure

```
Storage/
├── Models/          # Data models (Item, Location, etc.)
├── ViewModels/      # MVVM view models
├── Views/           # MAUI pages and UI
├── Services/        # Business logic and data access
├── Data/            # EF Core DbContext and repositories
└── Resources/       # Images, fonts, app resources
```

## Contributing

This is a personal learning project exploring mobile development. Feel free to fork and experiment.

## License

TBD