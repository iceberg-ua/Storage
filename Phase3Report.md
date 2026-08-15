# Phase 3 Implementation Report — Photo Capture & Management

Linear: VOL-27
Branch: `phase-3-photo-capture` (not pushed)
Date: 15 August 2026

Covers the Phase 3 implementation and the review follow-up that addressed five findings
against it.

---

## Summary

An in-app camera, a compression pipeline that keeps every file well under 1 MB, and
photo lifecycle handling that does not leak files. Android only.

- 19 tests passing (13 new, covering `PhotoService`)
- 2 target frameworks building clean (Android, Windows) with no new warnings
- 8 of the phase prompt's 11 acceptance criteria still need a physical device
- 1 scope item descoped (the detail-view photo has no page to live on)

A follow-up round addressed five review findings: EXIF orientation, quality-ladder
exhaustion, staged-photo cleanup, the MAUI version pin, and silent sweep failures.
Those are folded into the sections below.

---

## What shipped

**PhotoService and its interface** — `src/Storage.Core/Services/`
Owns filename generation, the size ceiling, path resolution, deletion and orphan
sweeping. The base directory is a constructor parameter, which is what lets the tests
point it at a temp folder. Unit tested.

**Compression pipeline**
Downsize to 1440px on the longest edge, encode JPEG at quality 0.75, stepping down
through 0.65 / 0.55 / 0.45 and stopping at the first result under 1 MB. If no rung gets
under the ceiling, the smallest candidate across all four is stored anyway and a warning
names the final size — losing the photo the user just took is worse than storing an
oversized one. The ladder and the ceiling are tested against a stub encoder; what the
real Android JPEG encoder produces has not been measured.

**EXIF orientation**
Android cameras routinely report rotation as an EXIF tag rather than rotated pixels, and
re-encoding drops the tag. Left alone that produces a photo that looks right in the
review step (which renders the original stream) and sideways everywhere afterwards
(which renders the re-encoded file). The Android compressor reads `TAG_ORIENTATION`
before decoding and bakes the rotation or flip into the pixels, covering all eight EXIF
orientations. Decoding uses `inSampleSize` first, because a full-resolution decode of a
12 MP capture is an out-of-memory risk.

**Camera page** — `src/Storage.App/Views/CameraPage.xaml`
Live preview, rear camera by default, shutter, cancel, and a review step with Retake
and Use photo. Written as code-behind rather than MVVM: almost all of it is direct
manipulation of the control and the page lifecycle, which a ViewModel would only have
to reach back into the view to do.

**Camera permission handling**
`CameraView` does not request `CAMERA` for you the way `MediaPicker` did, so the page
asks explicitly and separates the three outcomes: granted opens the camera, denied
shows a message, permanent denial offers a route into app settings. `CAMERA` is
declared in the Android manifest, with `android.hardware.camera` marked not required.

**Camera release on lifecycle events**
The preview stops when the page disappears and whenever the window stops, and restarts
on resume. A camera held by a backgrounded app will not reopen and can block other
apps, so this is the part most worth hammering on real hardware.

**Photo section on the item form**
Empty state is a tappable placeholder with Take photo and Choose from gallery; filled
state is a 120x120 rounded thumbnail with Replace and Remove.

**Staged-photo tracking** — `AddItemViewModel`
Keeps the saved filename separate from the staged one. Saving deletes only the file it
replaced, and only after the write succeeds.

Reconciliation runs from the edit page's `OnDisappearing`, not from the Cancel handler,
so Cancel, hardware back, the back gesture, Shell's own back arrow, and any exit route
added later are covered by construction rather than each having to remember. Two flags
say when a disappearance is a real departure: a committed flag the save path sets, and a
flag set before pushing the camera page so being covered does not count as leaving.

**List thumbnails**
48x48 with a neutral placeholder behind it, both in a fixed-size container so a row
with a photo is exactly as tall as one without.

**Cleanup on delete and at startup**
Deleting an item deletes its file. On launch the app reads every `PhotoPath` in the
database and removes any file in `photos/` nothing references — cheap insurance against
a crash mid-edit. The sweep reports how many it removed and logs both success and
failure; it is still fire-and-forget and still cannot take the app down.

It spares any file written in the last five minutes. A staged capture belongs to no item
yet, so the sweep cannot tell it from an orphan, and running unawaited at launch it can
overlap one — without the grace period a photo could be deleted out from under the edit
form. A genuine orphan comes from an earlier session and is never that new.

---

## Storage layout

Files land in `{AppDataDirectory}/photos/{guid:N}.jpg`, which is app-private on Android:
no storage permission needed, and removed on uninstall.

`Item.PhotoPath` holds the bare filename and never a path, so a stored value cannot go
stale if the OS moves the app's data directory. A test asserts the stored name contains
no directory separator.

---

## Where the plan met the package

The phase prompt warned that `CommunityToolkit.Maui.Camera` has shifted between releases
and said to check rather than assume. It had. All of the below was verified against the
installed 6.1.0 assembly.

| The prompt said | What 6.1.0 actually has |
|---|---|
| `.UseMauiCameraView()` | `.UseMauiCommunityToolkitCamera()` |
| A `Cameras` property to check for hardware | No such property; availability comes from `await GetAvailableCameras(token)` |
| Both toolkit packages are needed | The camera package is self-contained — its only dependencies are Microsoft.Maui.Controls and AndroidX, so the main toolkit is not referenced |
| `MediaPicker.PickPhotoAsync()` for gallery import | Obsolete in MAUI 10; replaced with `PickPhotosAsync` capped at `SelectionLimit = 1`, which opens the same picker |
| — | The package requires Microsoft.Maui 10.0.60. No available workload set ships a MAUI manifest that new, so `MauiVersion` is pinned in the app csproj — see Gaps and risks |
| One `PhotoService` calling `PlatformImage` directly | `PlatformImage` exists only in the platform-specific builds of Microsoft.Maui.Graphics, so it cannot be reached from the plain `net10.0` target the tests compile against |

### Why there are two extra interfaces

That last row is the one architectural decision worth defending. The prompt asked for a
testable `PhotoService` *and* for it to use `PlatformImage`. Those two requirements pull
apart: the test project targets plain `net10.0`, where neither `PlatformImage` nor
`MediaPicker` exists.

So the two platform-bound operations sit behind one-method seams — `IImageCompressor`
and `IGalleryPicker` — implemented in the app project and stubbed in tests. That keeps
the policy (naming, the ceiling, the ladder, the filesystem) in one testable class and
confines the untestable part to two files that do nothing but call the platform. Given
the project's standing preference against unnecessary abstraction, two one-method
interfaces is the smallest seam that satisfies both requirements.

---

## Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Live preview, review step, photo persists across restart | Needs device |
| 2 | Every saved file under 1 MB; typical under 300 KB | Ceiling and exhaustion tested, real sizes not measured |
| 3 | Photos visually clean at full width | Needs device |
| 4 | `PhotoPath` is a bare unique filename | Unit tested |
| 5 | Denying permission shows a message and returns cleanly | Needs device |
| 6 | Permanent denial offers a route to app settings | Needs device |
| 7 | Re-entering the camera page and backgrounding both survive | Needs device |
| 8 | Deleting an item leaves no file behind | Sweep tested, flow needs device |
| 9 | Replacing a photo leaves exactly one file | Needs device |
| 10 | Cancelling after a capture leaves no orphan | Needs device |
| 11 | List renders a mix of photo and no-photo items | Needs device |

Criteria 9 and 10 are ViewModel logic, which the phase prompt explicitly excluded from
testing. The code is written to satisfy them, but nothing automated is watching.

---

## Tests

Twelve new tests against a temp directory, in `tests/Storage.Tests/PhotoServiceTests.cs`:

- The photo directory is created on first write, not before
- Saved names are unique, end in `.jpg`, and contain no directory separator
- The documented pixel bound is what gets requested of the encoder
- A first encode already under the ceiling is kept, with no wasted re-encodes
- An oversized encode steps down the ladder and stops at the first rung that fits
- A ladder that never gets under the ceiling still writes a file, keeps the smallest
  candidate rather than merely the last, and logs a warning
- A forward-only source stream — what the camera hands over — can still be re-read by
  the ladder, because the service buffers it first
- Path resolution is null-safe in both directions
- Deleting is idempotent: twice, unknown, and null are all no-ops
- Orphan cleanup removes unreferenced files only, and tolerates a missing directory
- A file written moments ago survives the sweep even when nothing references it; the
  same file backdated past the grace period is removed
- Gallery import stores the picked image, and returns null when cancelled without
  creating anything

Deliberately not tested, per the phase prompt: ViewModels, the camera page, and UI.

The package API corrections are recorded in `.claude/CLAUDE.md` so later phase prompts do
not repeat the stale names.

## Temporary — remove after the device pass

A `#if DEBUG` log line in `PhotoService.SaveAsync` reports each saved photo's byte count
and the quality rung used, so real encoder output can be read off the log rather than
pulled out of app-private storage. It comes out once the numbers are confirmed.

---

## Gaps and risks

**The detail-view photo has nowhere to go.** Scope called for a full-width photo above
the details on the item detail view. The app has no detail view — tapping an item in the
list opens the edit form directly. Building one would insert a navigation step between
list and edit, which is a product decision well outside a photo phase, so I left it. The
edit form's photo section covers the need for now.

**The MAUI version pin stays, deliberately.** The workload was updated to set 10.0.303.1,
the newest for the 10.0.300 SDK band; its MAUI manifest is still 10.0.20, and building
without the pin fails with `NU1605` against the camera package's 10.0.60 floor. MAUI
10.0.x services through NuGet ahead of the workload manifest, so setting `MauiVersion` is
the supported mechanism rather than a workaround. Both target frameworks build clean.
Revisit when a workload set ships a manifest at 10.0.60 or later.

**`OnDisappearing` is assumed not to fire on app backgrounding.** Reconciliation hangs on
that assumption — it holds in MAUI on Android, where page appearance is driven by
navigation rather than the activity lifecycle, which is also why `CameraPage` has to hook
`Window.Stopped` separately. A stack-membership check was considered instead and
rejected: removal timing during a pop is not guaranteed, and getting it wrong would
silently reintroduce the leak rather than fail loudly. Confirm on device.

---

## Deliberately out of scope

No iOS support, `Info.plist` entries or platform branches; no multiple photos per item;
no cropping, rotation, flash or zoom controls; no fullscreen viewer or pinch-zoom; no
video; no AI, which is Phase 4's job.

---

## What to do next

Deploy to a physical Android phone and work the eight remaining criteria, roughly in
order of how much they would hurt to get wrong:

1. Take one photo in portrait and one in landscape, and check the review step, the edit
   form preview, the 48x48 list thumbnail, and the same again after an app restart. A
   sideways thumbnail is the single most likely visible defect in the phase.
2. Open the camera, leave, and re-open it several times; then background the app with
   the camera live and come back. A dead preview here is the failure mode that matters
   most.
3. Read the debug log for each saved photo's byte count and quality rung, and look at a
   photo full width for blockiness. Remove the instrumentation once the numbers hold.
4. Leave the edit page after a capture by every route — Cancel, hardware back, the
   gesture, and Shell's back arrow — and confirm no orphan is left in `photos/` by any
   of them.
5. Stage a photo, background the app from the edit form, and return; the staged photo
   must survive. This is the assumption reconciliation rests on.
6. Deny the camera permission, then deny it permanently, and confirm both paths return
   cleanly and the second offers settings.
7. Replace a photo and confirm exactly one file remains.

Once that passes, Phase 3 is genuinely done and Phase 4 has real photos to run inference
against.
