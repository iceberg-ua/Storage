# Implementation Phases

Android-first development. Each phase builds on the previous one.

---

## Phase 1: Foundation
**Goal:** Add items manually, store them, browse the list

- Project setup (MAUI, SQLite + EF Core, DI)
- `Item` model (Id, Name, Description, CreatedAt)
- `Location` model (Id, Name, ParentId for hierarchy)
- Basic CRUD for items and locations
- Simple list view of items
- Add/edit item form (manual text entry only)
- Assign item to location

**Deliverable:** You can create locations, add items with names/descriptions, and see them in a list.

---

## Phase 2: Camera Integration
**Goal:** Capture photos for items

- Camera permissions handling (Android-specific)
- Take photo via MAUI Community Toolkit
- Store photos locally (file system, path in DB)
- Display item photo in list and detail views
- Multiple photos per item

**Deliverable:** Items have photos attached.

---

## Phase 3: On-Device AI Recognition
**Goal:** AI suggests item details from photo

- Integrate ONNX Runtime or ML.NET
- Bundle a lightweight image classification model (MobileNet or similar)
- After photo capture â†’ run inference â†’ suggest name/category
- User can accept, edit, or reject suggestions

**Deliverable:** Snap a photo, get an auto-suggested name.

---

## Phase 4: Search & Filtering
**Goal:** Find items quickly

- Full-text search (SQLite FTS or simple LIKE queries)
- Filter by location, category, date range
- "Recently added" view
- Browse by location hierarchy

**Deliverable:** Usable search that scales to hundreds of items.

---

## Phase 5: Tags & Polish
**Goal:** Flexible organization + UX improvements

- Custom tags per item
- Bulk operations (move multiple items)
- Empty states, loading indicators, error handling
- Basic settings screen

**Deliverable:** Feels like a complete app.

---

## Phase 6: Data Safety
**Goal:** Don't lose user data

- Export to JSON/ZIP (items + photos)
- Import from backup
- Auto-backup option to device storage

**Deliverable:** Users can back up and restore.

---

## Status

| Phase | Status |
|-------|--------|
| Phase 1: Foundation | Not Started |
| Phase 2: Camera | Not Started |
| Phase 3: AI Recognition | Not Started |
| Phase 4: Search | Not Started |
| Phase 5: Tags & Polish | Not Started |
| Phase 6: Data Safety | Not Started |