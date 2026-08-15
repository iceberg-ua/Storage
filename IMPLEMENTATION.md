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
- Camera capture UI not yet wired — `PhotoPath` exists on the model but no capture flow (this is now explicitly Phase 3 scope)
- Item and location editing were initially missing — added via a follow-up Claude Code prompt
- Quantity stepper and "add item from location screen" were also follow-up additions, now both delivered

Linear: [VOL-18](https://linear.app/melnyk/issue/VOL-18)

---

## Phase 2: Design & Visual Identity — Not Started (current)

Linear: VOL-20 (Todo)

No implementation work started yet.

---

## Phase 3: Photo Capture & Management — Not Started

Blocked on Phase 2 (or can run in parallel — not yet decided). Prerequisite for Phase 4 (AI recognition needs real photos to run inference against).

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
