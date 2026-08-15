# Storage

A local-first mobile inventory app that helps you photograph, tag, and organize your belongings across any storage space — from cellars to closets.

## Overview

Storage helps you keep track of everything you own and where it's stored. Take a photo, add a description and tags, assign it to a location, and find it later with search. All data stays on your device — no cloud, no accounts, complete privacy.

## Problem Statement

People accumulate belongings across multiple storage spaces — basements, attics, closets, garages, sheds, storage units. Over time, it becomes impossible to remember what's stored where. This leads to:

- Buying duplicates of items you already own
- Wasting time searching for things
- Forgetting valuable items exist
- Difficulty with home inventory for insurance
- Chaos when moving or reorganizing

## Solution

A simple mobile app that makes cataloging possessions effortless:

1. **Snap** — Take a photo of any item
2. **Describe** — Add a name, description, and tags
3. **Organize** — Assign to a location
4. **Find** — Search your inventory anytime

## Core Features (MVP)

### Item Management
- Capture one photo per item with device camera (model field delivered in Phase 1; capture UI lands in Phase 3)
- Name and description (manual entry)
- Free-text tags (comma-separated)
- Quantity (with stepper control)

### Location System
- Locations support nesting (flat or hierarchical, e.g. "Garage" → "Shelf 2")
- Assign item to a location
- Add an item directly from a location's edit screen
- Location-based grouping in item list

### Search & Discovery
- Search across description and tags (LIKE-based)

### Privacy & Data
- All data stored locally on device
- No internet connection required
- No user accounts

## Planned (Post-MVP)

- On-device AI category suggestion from photo
- Multiple photos per item
- Export/import for backup

## Technical Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET MAUI |
| Language | C# |
| UI | XAML |
| Database | SQLite + Entity Framework Core |
| AI/ML | ML.NET / ONNX Runtime (post-MVP) |
| Camera | MAUI Community Toolkit |

## Target Platforms

- Android (primary)
- iOS (secondary)

## License

TBD
