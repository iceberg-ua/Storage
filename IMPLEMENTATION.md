# Implementation Status

Tracks what's actually built, as opposed to what's planned (see PHASES.md) or in scope (see PROJECT.md). Update this after every implementation session.

---

## Phase 1: MVP — ✅ Complete

**Delivered:**
- Project setup: .NET MAUI, SQLite + EF Core, DI
- `Item` model: Id, Name, Description, Tags (free-text, comma-separated), PhotoPath (reserved, not yet wired), Quantity, LocationId, CreatedAt
- `Location` model: Id, Name, ParentId — nested hierarchy pulled forward from original Phase 3 scope
- Add/edit item form (name, description, tags, quantity, assign location)
- Add/edit location form (name, optional parent)
- "Add item" shortcut from a location's edit screen
- List view grouped by location, with Items/Locations tab strip for locations that have children
- Search: LIKE-based query across Description + Tags
- Quantity field with custom stepper control
- Dark theme readability fix (white-on-white titles resolved)

**Known gaps carried forward (addressed during Phase 1 device testing, tracked as they were found):**
- Item and location editing were initially missing — added via a follow-up Claude Code prompt
- Quantity stepper and "add item from location screen" were also follow-up additions, now both delivered

Linear: [VOL-18](https://linear.app/melnyk/issue/VOL-18)

---

## Phase 2: Design & Visual Identity — Not Started (current)

Linear: VOL-20 (Todo)

No implementation work started yet.

---

## Phase 3: Photo Capture & Management — ✅ Complete

Ran ahead of Phase 2 rather than after it. Prerequisite for Phase 4 (AI recognition needs real photos to run inference against).

**Delivered:**
- `IPhotoService` / `PhotoService` (Storage.Core/Services): save, gallery import, path resolution, delete, orphan cleanup. Base directory is a constructor parameter, so it is testable against a temp folder
- Compression pipeline: downsize to 1440px on the longest edge, JPEG at quality 0.75, stepping down through 0.65 / 0.55 / 0.45 until the file is under the 1 MB ceiling
- Photos stored at `{AppDataDirectory}/photos/{guid:N}.jpg`; `Item.PhotoPath` holds the bare filename and is resolved at read time
- In-app camera page (`CameraPage`) using `CommunityToolkit.Maui.Camera`: live preview, rear camera by default, shutter, cancel, and a review step with Retake / Use photo
- Manual `CAMERA` permission handling, distinguishing granted / denied / permanently denied (the last offers `AppInfo.ShowSettingsUI()`); `CAMERA` declared in the Android manifest with `android.hardware.camera` marked not required
- Camera released on `OnDisappearing` and whenever the app's window stops, and restarted on resume
- No-camera-hardware fallback: the page says so and offers the gallery instead of a dead preview
- Photo section on the item edit page: placeholder with Take photo / Choose from gallery, or a 120x120 rounded thumbnail with Replace / Remove
- Pending-photo tracking in `AddItemViewModel`: a capture that is never saved is deleted on cancel (including hardware back), and the previous file is deleted only after a successful save
- 48x48 thumbnail in the item list with a neutral placeholder, sized so rows do not shift between the two states
- Photo file deleted when its item is deleted; `CleanupOrphansAsync` runs once at startup against the set of `PhotoPath` values in the DB
- 12 xUnit tests for `PhotoService` covering unique bare filenames, the size ceiling and quality ladder, forward-only source streams, idempotent delete, and orphan cleanup

**Deviations from the phase prompt (API had moved on):**
- Registration is `.UseMauiCommunityToolkitCamera()`, not `.UseMauiCameraView()`
- `CameraView` has no `Cameras` property in 6.1.0; availability comes from `await GetAvailableCameras(token)`
- Only `CommunityToolkit.Maui.Camera` is referenced — it is self-contained and does not need the main `CommunityToolkit.Maui` package
- `MediaPicker.PickPhotoAsync()` is obsolete in MAUI 10; the gallery path uses `PickPhotosAsync` with `SelectionLimit = 1`
- The camera package requires Microsoft.Maui 10.0.60, so `MauiVersion` is pinned in the app csproj (the installed workload defaults to 10.0.20)
- Platform image work sits behind `IImageCompressor` / `IGalleryPicker`, because `PlatformImage` and `MediaPicker` do not exist on the plain `net10.0` target the test project builds against
- The "photo at full width on the item detail view" item has no page to land on: tapping an item in the list opens the edit form, and the app has no separate detail view. The edit form's photo section covers it for now

Linear: VOL-27

---

## Phase 4: On-Device AI Recognition — Not Started

---

## Phase 5: Tags & Polish — Not Started

---

## Phase 6: Data Safety — Not Started

---

## Doc Cleanup Notes

- `Implementation_plan.md` (old status doc) was removed from the repo for being stale. This file (`IMPLEMENTATION.md`) replaces it as the single build-status source of truth.
- PHASES.md is the roadmap (what's planned per phase); this file is the status (what's actually done).
- PROJECT.md is the scope document (what's in vs. out of the product); it should only change when scope changes, not on every implementation update.
