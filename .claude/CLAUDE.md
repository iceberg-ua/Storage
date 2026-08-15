# Claude Instructions for Storage Project

## Project Context

Storage is a local-first mobile inventory app built with .NET MAUI. The user is an experienced C# developer but new to mobile development and ML integration.

## Communication Style

- Be direct and concise â€” skip unnecessary preambles
- Explain mobile-specific concepts when they appear (lifecycle, permissions, platform differences)
- Don't over-explain C# basics â€” the user knows the language
- When introducing new concepts, give a one-sentence explanation, then show code

## Important
- Follow the phases in PHASES.md document

## Code Guidelines

- Provide complete, working code snippets - not pseudocode
- Use modern C# features (records, pattern matching, null-coalescing, file-scoped namespaces)
- Follow .NET naming conventions (PascalCase for public members, _camelCase for private fields)
- Keep classes focused and small
- Prefer composition over inheritance
- Include relevant using statements in examples

## Architecture Preferences

- Keep it simple - no unnecessary abstractions for an MVP
- Use MVVM pattern (standard for MAUI)
- Repository pattern for data access
- Dependency injection via MAUI's built-in container
- Avoid premature optimization

## When Suggesting Solutions

- Start with the simplest approach that works
- Flag if something is overengineered for current needs
- Mention trade-offs briefly, then recommend one path
- If multiple valid approaches exist, pick one and explain why

## Technical Constraints to Remember

- Everything must work offline â€” no cloud dependencies
- SQLite is the database â€” use EF Core
- Target Android first, iOS second
- AI features use ML.NET or ONNX Runtime
- Camera handling via MAUI Community Toolkit

## Package APIs — verified, do not guess

These were wrong in an earlier phase prompt and cost a round of rework. Use these names.

**CommunityToolkit.Maui.Camera 6.1.0**
- Register with `.UseMauiCommunityToolkitCamera()` — *not* `.UseMauiCameraView()`
- `CameraView` has no `Cameras` property. Get hardware with `await GetAvailableCameras(token)`
- Events are `MediaCaptured` (`MediaCapturedEventArgs.Media`) and `MediaCaptureFailed`
  (`MediaCaptureFailedEventArgs.FailureReason`)
- Preview control is `StartCameraPreview(token)` / `StopCameraPreview()`
- The package is self-contained. It does **not** need the main `CommunityToolkit.Maui`
  package — its only dependencies are Microsoft.Maui.Controls and AndroidX

**MAUI 10 Essentials**
- `MediaPicker.PickPhotoAsync()` is obsolete. Use `PickPhotosAsync` with
  `MediaPickerOptions { SelectionLimit = 1 }` — same picker, one result

**Microsoft.Maui.Graphics**
- `PlatformImage` exists only in the platform-specific builds. It is unreachable from a
  plain `net10.0` library, which is why image work sits behind `IImageCompressor`

Always check an installed package's actual surface before writing against it, and say so
if it differs from what a prompt assumed rather than working around it silently.

## What to Avoid

- Don't suggest cloud-based AI services (Google Vision, Azure AI, etc.)
- Don't recommend switching frameworks mid-project
- Don't add features not in PROJECT.md without asking
- Don't create overly complex folder structures for a small app
- Don't use third-party libraries when built-in options work fine

## When Stuck or Unclear

- Ask one clarifying question, then proceed with reasonable assumptions
- If a MAUI limitation exists, say so directly and offer workarounds
- If something is experimental or poorly documented, warn about it

## File Organization

- Keep related code together
- Match typical MAUI project structure (Models, ViewModels, Views, Services)
- One class per file unless tightly coupled (e.g., DTO + enum)

## Testing Approach

- Suggest unit tests for business logic and services
- Don't over-test ViewModels or UI code in MVP phase
- Use xUnit (standard for .NET)