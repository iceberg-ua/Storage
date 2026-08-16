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
- The sweep spares any file written in the last 5 minutes (`PhotoService.OrphanGracePeriod`). A staged capture is in no item yet, so it is indistinguishable from an orphan, and the sweep runs unawaited at launch where it can overlap one — the grace period stops it deleting a photo the user is still holding. A genuine orphan comes from an earlier session and is never that new
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

## Phase 5: Tags & Polish — 🚧 Tags done, polish outstanding

**Delivered (VOL-29):**
- `Tag` entity (Id, Name) replacing the free-text comma-separated `Item.Tags` string. `Item.Tags` is now `ICollection<Tag>`
- Many-to-many through an `ItemTag` join table with cascade deletes. EF supplies the join itself — nothing hangs off the relationship, so there is no `ItemTag` entity class, only the column names configured in `StorageDbContext`
- Case-insensitive de-duplication enforced at the database: `Tag.Name` is `COLLATE NOCASE` with a unique index, so "Tools" and "tools" are one row and the casing seen first wins. The repository and ViewModel collapse case-variants the same way, so the UI never shows the two as separate chips
- `AddTags` migration, hand-ordered so the data migration runs while `Items.Tags` still exists: create `Tags` + unique index → create `ItemTag` → split the old comma-separated values with a recursive CTE (SQLite has no split function) → drop `Items.Tags`. `Down()` folds the assignments back into the column with `group_concat` before dropping the tables
- The old column was dropped rather than kept as a fallback: `git log -S "Tags" -- src/Storage.App` shows no app code ever wrote to it, so there was no real data to hedge against. The split still runs, for any hand-seeded database
- `ITagRepository` / `TagRepository`: `GetAllAsync` and `SetItemTagsAsync(itemId, names)`, which resolves each name to an existing row or creates it, in one query for the whole set
- Tag input on the item edit form: type and press return to add, autocomplete against tags already in use (top 5, filtered as you type), tap a chip to remove. A tag left half-typed in the entry is still committed on save rather than silently dropped
- Read-only tag chips on each row of the item list
- `ItemRepository.UpdateAsync` now sets `Entry(item).State = Modified` instead of `Update(item)` — the latter walks the graph, and with tags included it would mark the tag and join rows modified too
- 11 tests: 8 covering assignment, replacement, case-insensitive de-dup, blank names, and orphan retention; 3 covering the migration itself

**Deliberate behaviour — orphan tags are kept.** A tag left on no item stays in the database and in autocomplete; reuse is the point of the feature, and global rename/merge/delete belongs to the tag management screen, which VOL-29 puts out of scope.

**Testing note:** the other fixtures build their schema with `EnsureCreated()`, which reads the model and never runs migration SQL. `MigrationTests` therefore migrates to `AddItemQuantity`, inserts legacy comma-separated rows, then migrates forward — the only way to prove the "no tag data lost" requirement holds.

**Delivered (tag management + colours, follow-on to VOL-29):**
- `Tags` tab — a third `ShellContent` alongside Locations and Items — listing every tag with its colour swatch and the number of items it is on. Swipe to delete, tap to edit, ➕ to add. Modelled directly on `LocationsPage`
- `EditTagPage` handles both add and edit, following the `AddLocationViewModel` shape: one ViewModel, optional `tagId` query parameter
- Renaming onto an existing name is **rejected** with a message rather than merged. `ITagRepository.NameExistsAsync` checks first so the clash reads as a sentence instead of surfacing from the unique index as a `SqliteException`. Merging tags remains out of scope
- Deleting a tag names the item count in the confirmation — the join-row cascade is otherwise invisible from that screen — and leaves the items themselves untouched
- `TagPalette` (Storage.Core): ten fixed colours, all dark enough that white chip text clears contrast in both themes. That is why chips carry no luminance calculation and no theme-dependent text colour
- A new tag gets the next palette colour automatically, whether typed on an item form or added from the Tags tab, so tags are distinguishable with no extra step. `SetItemTagsAsync` walks the index forward per tag created, so two new tags in one save don't share a swatch
- `AddTagColor` migration backfills existing tags across the palette by `Id % 10` rather than leaving them all default grey. The hex values are written into the migration as literals, not read from `TagPalette` — a migration has to keep producing the same result after the palette is edited
- Chips are coloured in the item list and on the item form; autocomplete rows show the tag's colour as a dot so a suggestion is recognisable as the chip it will become
- `AddItemViewModel.ItemTags` holds `Tag` objects rather than strings, since a string carries no colour. A tag typed for the first time gets a detached `Tag` coloured by the same rule the repository will use, so the chip doesn't change colour once saved
- 8 further tests, including the colour backfill migrating a real legacy database forward

**Palette lives in Storage.Core, not the app.** It is plain string data with no MAUI dependency, and the repository needs it to assign colours at creation time.

**Still outstanding in this phase:** bulk operations (move multiple items), settings screen, remaining empty/loading/error-state polish.

**Known cosmetic bug, pre-existing:** `AppShell.xaml` references `folder.png` and `box.png` as tab icons but `Resources/Images` contains only `dotnet_bot.png`. Both tabs render iconless and Glide logs a `FileNotFoundException` per launch. The Tags tab was added without an icon to match. Worth fixing with a real icon set.

**Correction to the Phase 1 entry above:** it lists a tags field on the item form and LIKE-based search across Description + Tags as delivered. Neither was ever in the code — the `Tags` column existed but nothing read or wrote it, and there is no search yet.

Linear: [VOL-29](https://linear.app/melnyk/issue/VOL-29)

---

## Phase 6: Data Safety — Not Started

---

## Doc Cleanup Notes

- `Implementation_plan.md` (old status doc) was removed from the repo for being stale. This file (`IMPLEMENTATION.md`) replaces it as the single build-status source of truth.
- PHASES.md is the roadmap (what's planned per phase); this file is the status (what's actually done).
- PROJECT.md is the scope document (what's in vs. out of the product); it should only change when scope changes, not on every implementation update.
- **Phase 1 leftover resolved (during Phase 3):** the app initialized the database twice, `Migrate()` in `MauiProgram` followed by `EnsureCreated()` in `App`'s constructor. The ordering made the second a no-op, so nothing was broken, but the two are mutually exclusive strategies — `EnsureCreated` builds a schema with no `__EFMigrationsHistory` table, so any reordering would have silently stopped migrations applying, surfacing much later as a missing column. `EnsureCreated` and its wrapper are gone; `Migrate()` is the single initialization path. It remains in the repository tests, where it is the right tool for an in-memory database.
