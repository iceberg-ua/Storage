# Storage

A local-first mobile inventory app that uses on-device AI to photograph, recognize, and organize your belongings across any storage space - from cellars to closets.

## Overview

Storage helps you catalog and find items in your home by combining photos, AI-powered recognition, and flexible organization. Everything works offline with on-device processing - no cloud dependencies.

## Tech Stack

- **.NET MAUI** - Cross-platform mobile framework (Android-first, iOS second)
- **SQLite + EF Core** - Local database for offline-first storage
- **MAUI Community Toolkit** - Camera integration
- **ML.NET / ONNX Runtime** - On-device AI for image recognition
- **C# 12+** - Modern language features (records, pattern matching, file-scoped namespaces)

## Architecture

- **MVVM pattern** - Standard MAUI architecture
- **Repository pattern** - Data access layer abstraction
- **Dependency injection** - Built-in MAUI container
- **Local-first** - All data stored on device, no cloud sync

## Features (Planned)

- 📸 Photo capture and attachment to items
- 🤖 AI-powered item recognition from photos
- 🗂️ Hierarchical location organization
- 🔍 Full-text search and filtering
- 🏷️ Custom tags for flexible organization
- 💾 Export/import for data backup

## Development Phases

See [PHASES.md](.claude/PHASES.md) for detailed implementation roadmap.

Current status: **Phase 1 - Foundation** (Not Started)

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

This is a personal learning project exploring mobile development and on-device AI. Feel free to fork and experiment.

## License

TBD
