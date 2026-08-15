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
- Compression pipeline: downsize to 1440px on the longest edge, JPEG at quality 0.75, stepping down through 0.65 / 0.55 / 0.45 until the file is under the 1 MB ceiling. An exhausted ladder stores the smallest candidate and logs a warning — never throws away the capture
- EXIF orientation is baked into the saved pixels on Android (`ExifInterface` read, `Matrix` rotate/flip before encoding), so a photo that the camera tagged as rotated is upright everywhere, not just in the review step
- Photos stored at `{AppDataDirectory}/photos/{guid:N}.jpg`; `Item.PhotoPath` holds the bare filename and is resolved at read time
- In-app camera page (`CameraPage`) using `CommunityToolkit.Maui.Camera`: live preview, rear camera by default, shutter, cancel, and a review step with Retake / Use photo
- Manual `CAMERA` permission handling, distinguishing granted / denied / permanently denied (the last offers `AppInfo.ShowSettingsUI()`); `CAMERA` declared in the Android manifest with `android.hardware.camera` marked not required
- Camera released on `OnDisappearing` and whenever the app's window stops, and restarted on resume
- No-camera-hardware fallback: the page says so and offers the gallery instead of a dead preview
- Photo section on the item edit page: placeholder with Take photo / Choose from gallery, or a 120x120 rounded thumbnail with Replace / Remove
- Staged-photo reconciliation runs from the edit page's `OnDisappearing`, gated on a committed flag, so Cancel / hardware back / gesture back / Shell's back arrow — and any exit route added later — are all covered by construction rather than each remembering to clean up. Pushing the camera page on top does not trigger it
- 48x48 thumbnail in the item list with a neutral placeholder, sized so rows do not shift between the two states
- Photo file deleted when its item is deleted; `CleanupOrphansAsync` runs once at startup against the set of `PhotoPath` values in the DB, returns how many it removed, and logs both success and failure instead of swallowing them
- 13 xUnit tests for `PhotoService` covering unique bare filenames, the size ceiling, the quality ladder including exhaustion, forward-only source streams, idempotent delete, and orphan cleanup

**Deviations from the phase prompt (API had moved on):**
- Registration is `.UseMauiCommunityToolkitCamera()`, not `.UseMauiCameraView()`
- `CameraView` has no `Cameras` property in 6.1.0; availability comes from `await GetAvailableCameras(token)`
- Only `CommunityToolkit.Maui.Camera` is referenced — it is self-contained and does not need the main `CommunityToolkit.Maui` package
- `MediaPicker.PickPhotoAsync()` is obsolete in MAUI 10; the gallery path uses `PickPhotosAsync` with `SelectionLimit = 1`
- `MauiVersion` is pinned to 10.0.60 in the app csproj, deliberately. The camera package requires Microsoft.Maui.Controls >= 10.0.60; the newest MAUI workload manifest (workload set 10.0.303.1, latest for the 10.0.300 SDK band) still pins 10.0.20, and building without the pin fails with NU1605. MAUI 10.0.x services through NuGet ahead of the workload manifest, so this is the supported mechanism rather than a workaround. Revisit when a workload set ships a manifest at 10.0.60 or later
- Platform image work sits behind `IImageCompressor` / `IGalleryPicker`, because `PlatformImage` and `MediaPicker` do not exist on the plain `net10.0` target the test project builds against

These corrections are recorded in `.claude/CLAUDE.md` so later phase prompts don't repeat the stale names.

**Descoped:**
- *Photo at full width on the item detail view.* The app has no item detail view — tapping an item in the list opens the edit form directly. Adding one is a navigation decision, not a photo one, so it is out of Phase 3 rather than outstanding in it. The edit form's photo section serves the need.

**Temporary, remove after the device pass:**
- A `#if DEBUG` log line in `PhotoService.SaveAsync` reporting each saved photo's byte count and the quality rung used, so real encoder output can be read off the log instead of pulled out of app-private storage

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
