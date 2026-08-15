# Claude Code Prompt — Phase 3: Photo Capture & Management

**Linear:** VOL-27
**Depends on:** Phase 1 (complete). `PhotoPath` already exists on the `Item` model — no migration needed.
**Platform:** Android only for this phase. Do not add iOS-specific config or conditional code; iOS is deferred.

---

## Goal

A user can capture a photo with a camera preview inside the app, review it before committing, and attach it to an item. Photos are compressed to well under 1 MB, stored as files in app-private storage under a unique id, and shown in the list and detail views.

---

## Technical decisions (already made — implement these, don't re-litigate)

### In-app camera via CommunityToolkit `CameraView`

Use `CommunityToolkit.Maui.Camera` (separate NuGet package from `CommunityToolkit.Maui` — both are needed). Register with `.UseMauiCameraView()` in `MauiProgram`.

**Warning — verify before writing code:** this control is relatively new and its API surface has shifted between releases; the property and event names below are the shape to aim for, not a guarantee. Check the installed package version's actual API (`CameraView`, `MediaCaptured`, `CaptureImage`, `SelectedCamera`, `Cameras`) and adapt rather than assuming. If something is missing in the pinned version, say so instead of working around it silently.

Gallery import stays on the built-in `MediaPicker.PickPhotoAsync()` — no reason to hand-roll a picker.

### Permissions are now manual

This is the main behavioural difference from `MediaPicker`: `MediaPicker` requested the `CAMERA` permission for you, `CameraView` does not. Request it explicitly before the camera page renders:

```csharp
var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
if (status != PermissionStatus.Granted)
    status = await Permissions.RequestAsync<Permissions.Camera>();
```

Handle three outcomes distinctly: granted → open camera; denied → message and return; permanently denied (`ShouldShowRationale` false after a denial) → message pointing the user at app settings, with `AppInfo.ShowSettingsUI()`.

### Store a filename, not an absolute path

`PhotoPath` holds **only the filename** (e.g. `a3f9c2e1....jpg`), never a full path. Resolve at read time:

```csharp
Path.Combine(FileSystem.AppDataDirectory, "photos", item.PhotoPath);
```

Photos live in `{FileSystem.AppDataDirectory}/photos/`, filename `{Guid.NewGuid():N}.jpg`. `AppDataDirectory` is app-private on Android — it needs no storage permission and is removed on uninstall, which is what we want for a local-first app.

### Compression: hard ceiling of 1 MB, target ~200 KB

Raw captures run 4–12 MB. Compress in `PhotoService` before writing to disk:

1. Downsize to **1440px on the longest edge**. Phone screens are ~1080px wide, so 1440 leaves headroom for detail without storing pixels nobody will see.
2. Encode JPEG at **quality 0.75**. Below ~0.7 the artifacts start showing on a modern phone display; 0.75 is about as aggressive as it goes cleanly.
3. If the result still exceeds 1 MB, re-encode at 0.65, then 0.55, then 0.45, stopping at the first pass under the limit. In practice step 2 lands at 150–250 KB and the loop never runs — it exists so a pathological input can't slip a huge file through.

Use the built-in `Microsoft.Maui.Graphics` API; no SkiaSharp dependency:

```csharp
using var image = PlatformImage.FromStream(sourceStream);
using var downsized = image.Downsize(1440, disposeOriginal: true);
await downsized.SaveAsync(destStream, ImageFormat.Jpeg, 0.75f);
```

Compress to a `MemoryStream` first so the size can be checked and the quality stepped down before anything touches the filesystem.

---

## Scope

### 1. `IPhotoService` / `PhotoService` (Services/)

```csharp
public interface IPhotoService
{
    Task<string> SaveAsync(Stream imageStream);  // compress + write; returns filename
    Task<string?> PickFromGalleryAsync();        // MediaPicker; compresses too; null if cancelled
    string? GetFullPath(string? fileName);       // null-safe resolve to absolute path
    Task DeleteAsync(string? fileName);          // no-op if null/missing
    Task CleanupOrphansAsync(IEnumerable<string> knownFileNames);
}
```

- Create the `photos/` directory on first write.
- Take the base directory as a constructor parameter rather than reading `FileSystem.AppDataDirectory` inline — that's what makes the service testable.
- Register as a singleton in `MauiProgram`.

### 2. Camera capture page (Views/)

A full-screen modal page, pushed from the item edit page:

- `CameraView` filling the page, rear camera selected by default.
- Shutter button; a cancel/back affordance that returns without saving.
- On capture, the page switches to a **review state**: the captured image shown full-screen with **Retake** and **Use photo**. Retake returns to the live preview. Use photo compresses, saves, and pops back with the filename.
- Handle the no-camera-hardware case (some emulators): if `Cameras` is empty, show a message and offer the gallery path instead of rendering a dead preview.

**Lifecycle note:** the camera must be released when the page disappears — on `OnDisappearing`, and also when the app is backgrounded while the page is open. A held camera that isn't released will fail to reopen and can block other apps.

### 3. Item edit page

A photo section above the name field:

- No photo: placeholder tap target with **Take photo** (opens the camera page) and **Choose from gallery**.
- Photo present: preview thumbnail (~120x120, `Aspect="AspectFill"`, rounded corners) with **Replace** and **Remove**.
- Photo changes follow the page's existing save semantics. Track the pending filename in the ViewModel: delete the orphan if the user cancels the edit; delete the previous file only after a successful save when replacing.

### 4. Display

- **Item list:** small thumbnail (~48x48) where a photo exists, neutral placeholder icon where it doesn't. Row height must not shift between the two.
- **Item detail:** photo at full width above the details. No fullscreen viewer or zoom in this phase.

### 5. Cleanup

- Deleting an item deletes its photo file.
- Replacing a photo deletes the old file.
- Call `CleanupOrphansAsync` once on app startup with the set of `PhotoPath` values from the DB, removing any unreferenced file in `photos/`. Cheap insurance against crash-time leaks.

---

## Explicitly out of scope

- iOS support, `Info.plist` entries, or any `#if IOS` branches
- Multiple photos per item
- Any AI or on-device recognition (Phase 4)
- Cropping, rotation, filters, flash/zoom controls, or any image editing
- Fullscreen photo viewer, pinch-zoom, or a gallery view
- Video capture
- Visual restyling beyond fitting the photo controls in (Phase 2 owns design)
- Cloud storage, sync, or photo backup (Phase 6 owns backup)

---

## Acceptance criteria

1. Taking a photo on a physical Android device shows a live in-app preview, a review step, and attaches the photo to the item; it persists across app restart.
2. Every saved file is under 1 MB; typical captures land under 300 KB.
3. Photos are visually clean on the device screen at full width — no obvious blockiness or banding.
4. `Item.PhotoPath` in SQLite contains a bare filename with no directory separators, unique per photo.
5. Denying the camera permission shows a message and returns to the edit page — no crash, no half-saved item.
6. Permanently denying the permission offers a route to app settings.
7. Leaving the camera page and re-entering it works repeatedly; backgrounding the app with the camera open and returning does not leave a dead preview.
8. Deleting an item leaves no file behind in `photos/`.
9. Replacing a photo leaves exactly one file for that item.
10. Cancelling the edit page after taking a photo leaves the item's photo unchanged and no orphan file.
11. The item list renders correctly with a mix of photo and no-photo items.

---

## Testing

xUnit tests for `PhotoService` against a temp directory: save produces a unique filename, output is under 1 MB and within the pixel bound, delete is idempotent, orphan cleanup removes only unreferenced files. Don't test the ViewModels, the camera page, or UI.

---

## After implementation

Update `IMPLEMENTATION.md`: move Phase 3 to Complete with a delivered list, and drop the "camera capture UI not yet wired" gap from the Phase 1 carried-forward notes. Note in `README.md` that photo capture is Android-only for now.