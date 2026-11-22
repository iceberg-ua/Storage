# Design

The visual reference for the app: the "Snapshot" design, one static HTML mockup per screen.
**This folder is the source of truth for how a screen should look.** The design artifact on
claude.ai is a scratch space for trying ideas; whatever is chosen there is copied here.

## Viewing

Open any file in `screens/` in a browser. Each is a 390 × 844 phone screen with inline styles
and no scripts; links between screens work, so `home.html` is a starting point. The font
(Plus Jakarta Sans) loads from Google Fonts, so offline the text falls back to the system font.

## Screens

| Mockup | App code |
| --- | --- |
| `home.html` | `Views/HomePage.xaml` |
| `locations.html` | `Views/LocationsPage.xaml` |
| `location.html` | `Views/PlacePage.xaml`, `Views/PlaceHeader.xaml` |
| `location-with-photo.html` | the same, for a location with a photo (`Views/PlaceCover.xaml`) |
| `item.html` | `Views/ItemDetailPage.xaml` |
| `item-form.html` | `Views/ItemForm.xaml` in `CreatePage.xaml` and `AddItemPage.xaml` |
| `location-form.html` | `Views/LocationForm.xaml` in `CreatePage.xaml` and `AddLocationPage.xaml` |
| `pick-location.html` | `Views/PickPlacePage.xaml` |
| `snap.html` | `Views/CameraPage.xaml` (snap mode) |
| `find.html` | `Views/FindPage.xaml`, nothing typed |
| `find-results.html` | `Views/FindPage.xaml`, with a search |
| `tags.html` | `Views/TagsPage.xaml` |
| `edit-tag.html` | `Views/EditTagPage.xaml` |

## Tokens

`tokens.json` lists the design's colours (light theme) and the tag palette.
`tests/Storage.Tests/DesignTokenTests.cs` checks them against
`src/Storage.App/Resources/Styles/Colors.xaml` and `TagPalette`, and that every colour has a
`…Dark` variant. A colour change therefore touches `tokens.json`, `Colors.xaml` and, where it
shows, the mockups, in the same PR.

| Token | Light | Used for |
| --- | --- | --- |
| Canvas | `#F6F4EF` | page background |
| Surface | `#FFFFFF` | cards, fields, round icon buttons |
| SurfaceSunken | `#ECE8DF` | segment tracks, stepper minus, photo placeholders |
| Hairline | `#E4DFD4` | field outlines, dividers |
| Ink | `#1C1B19` | text, ink-filled buttons (Edit, New tag) |
| InkMuted | `#5F5B53` | captions, counts, paths |
| Primary | `#0F6E6E` | main buttons, focus ring, links |
| PrimarySoft / OnPrimarySoft | `#DCEDEA` / `#0B5555` | location chips, soft buttons |
| Danger | `#B3261E` | Delete, field errors |

Type is Plus Jakarta Sans: 30 ExtraBold for tab titles (`Display`), 22 ExtraBold for page
titles (`Title`), 18 Bold for sections, 13 Bold for field labels, 13 Regular for captions.
Corners: 16 for fields, 20–22 for cards, full pills for buttons and chips.

## Known differences

Where the app deliberately differs from the mockups. Fix the mockup or the code when one of
these is settled, and delete the line.

- **"Location", not "Place".** The mockups say Place(s); the app says Location(s). Undecided.
- **Tab bar.** The mockups have five slots with a raised camera button in the middle
  (Home, Places, Snap, Tags, Find). The app has four tabs (Home, Locations, Find, Tags) and a
  "📷 New item" button on the pages that need it.
- **Icons.** The mockups draw SVG icons; the app uses emoji and text glyphs (📷 ✎ ✕ ▾).
- **Home.** No settings button (there is no settings page yet).
- **Location page.** No "Recently added" sort menu and no Grid · List switch.
- **Item form.** Two dashed add tiles (camera, gallery) instead of one "+".
- **Location form.** The photo row has camera, gallery and Remove buttons instead of
  Change and Remove. The edit page has "Add item here" but not the "In this place" card with
  counts and Sub-place (Sub-place lives on the location's page).

## Changing the design

- A UI change updates the matching mockup in the same PR. The mockups are hand-written HTML
  with inline styles; edit them directly.
- If the code has to differ from the mockup (a MAUI limitation, say), update the mockup to
  what the code does, or add the difference to the list above.
- To explore something bigger, sketch it in the claude.ai design artifact first, then copy
  the chosen screens here.
