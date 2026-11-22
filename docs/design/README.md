# Design

The visual reference for the app: the "Snapshot" design, one static HTML mockup per screen.
**This folder is the source of truth for how a screen should look.** The design artifact on
claude.ai is a scratch space for trying ideas; whatever is chosen there is copied here.

## Viewing

Open any file in `screens/` in a browser. Each is a 390 × 844 phone screen with inline styles
and no scripts. Most buttons link to the screen they open, so `home.html` is a starting point;
a few lead nowhere (`href="#"`), such as "See all" on Home. The font
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
| the tab bar in `home.html`, `locations.html`, `tags.html` | `Views/AppTabBar.xaml` |
| `find.html` | `Views/FindPage.xaml`, nothing typed |
| `find-results.html` | `Views/FindPage.xaml`, with a search |
| `tags.html` | `Views/TagsPage.xaml` |
| `edit-tag.html` | `Views/EditTagPage.xaml` |

## Tokens

`tokens.json` lists the design's colours (light theme), the tag palette, and the few
mockup-only shades (photo stand-ins, the camera screen, web-only states), each with what the
app uses instead. `tests/Storage.Tests/DesignTokenTests.cs` checks that:

- every token matches `src/Storage.App/Resources/Styles/Colors.xaml`, and has a `…Dark` variant there
- the tag palette matches `TagPalette`
- every colour in `screens/*.html` is one of the listed ones

So a colour change touches `tokens.json` and whichever side it starts on, in the same PR. The
test does not check that a mockup uses the *right* token in a given place; that is review.

| Token | Light | Used for |
| --- | --- | --- |
| Canvas | `#F6F4EF` | page background |
| Surface | `#FFFFFF` | cards, fields, round icon buttons |
| SurfaceSunken | `#ECE8DF` | segment tracks, stepper minus (`#F0ECE4` in the mockups), photo placeholders |
| Hairline | `#E4DFD4` | dividers, outlined buttons and chips (fields have no outline until focused) |
| Ink | `#1C1B19` | text, ink-filled buttons (Edit, New tag) |
| InkMuted | `#5F5B53` | captions, counts, paths |
| Primary | `#0F6E6E` | main buttons, focus ring, links |
| PrimarySoft / OnPrimarySoft | `#DCEDEA` / `#0B5555` | location chips, soft buttons |
| Danger | `#B3261E` | Delete, field errors |

Icons are the mockups' own SVG paths, copied into `Controls/Icons.cs` and drawn by
`Controls/Icon` (or `Controls/IconButton` when they are tappable), so they take the theme's
colours. Shadows are the mockups' box-shadows, as the `…Shadow` resources in `Styles.xaml`.

Type is Plus Jakarta Sans: 30 ExtraBold for tab titles (`Display`), 22 ExtraBold for page
titles (`Title`), 18 Bold for sections, 13 Bold for field labels, 13 Regular for captions.
Corners: 16 for fields, 20–22 for cards, full pills for buttons and chips.

## Known differences

Where the app deliberately differs from the mockups. When one is settled, fix the mockup or the
code and delete the line. An open question (rather than a decided difference) belongs in a
`VOL-*` issue in Linear; link it from its line here.

**Everywhere**
- **"Location", not "Place".** The mockups say Place(s); the app says Location(s). Undecided:
  [VOL-75](https://linear.app/melnyk/issue/VOL-75/decide-location-or-place).
- **Find keeps the tab bar.** The mockups open Find from the tab bar onto a screen with Back
  and no tab bar; in the app Find is a tab like the others, so it has the bar and no Back.
  Find pushed from a location's page or the Locations tab has Back and no bar, as drawn.

**Home** (`home.html`)
- No settings button (there is no settings page yet).

**Locations** (`locations.html`)
- A search button in the header, and the button reads "New" rather than "New place".

**Location page** (`location.html`, `location-with-photo.html`)
- The header has ⋯ (edit, delete) where the mockup has a pencil.
- The path above the name is the whole way down ("Locations › Garage"), each step tappable;
  the mockup shows only the parent.
- The sub-location chips open that sub-location; there is no "All" chip and they do not filter
  the list. A dashed "+ Location inside" chip replaces the floating Sub-place button.
- One "New item here" button at the bottom instead of "Snap into Garage", in its own row
  under the list rather than floating over it.
- No "Recently added" sort menu and no Grid · List switch.
- Tiles show tag dots but not the sub-location name, and hide ×1.
- With a photo, the app shows it as a rounded card under the header, not full-bleed under a
  sheet.

**Item** (`item.html`)
- Photos swipe with dots instead of the thumbnail strip and "1 / 3".
- No "+ Tag" chip; tags are added in the form.
- The text section is headed "Description", not "Notes".
- Without a photo there is no sheet: the buttons and details sit on the canvas.

**Item form** (`item-form.html`)
- Two dashed add tiles (camera, gallery) instead of one "+".
- The box for the next tag sits under the chips rather than after them on the same line.

**Location form** (`location-form.html`)
- The photo row has camera, gallery and Remove buttons instead of Change and Remove.
- The edit page has "Add item here" but not the "In this place" card with counts and Sub-place
  (Sub-place lives on the location's page).

**Pick location** (`pick-location.html`)
- No "New place in Garage" row.
- A full page rather than a sheet over a dimmed page: a see-through modal page is
  unreliable in Shell on Android.

**Snap** (`snap.html`)
- No flash toggle.
- Taking a single photo for a form (not drawn) uses the same dark panel, with Gallery, the
  shutter and Cancel.

**Find** (`find.html`)
- With nothing typed, "Everything stored" is listed under recent searches and tags.
- Tag filters in force sit in a row under the box (the mockup only shows a place filter,
  which sits inside the box as drawn).
- "tag: a tag" picks from a list of tags; the mockup does not say what it opens.
- Results hide ×1 (`find-results.html`), as the location page's tiles do.

**Tag page** (`edit-tag.html`)
- "Custom colour…" opens RGB sliders under the palette; the mockup has the palette only.

## Changing the design

- A UI change updates the matching mockup in the same PR. The mockups are hand-written HTML
  with inline styles; edit them directly.
- If the code has to differ from the mockup (a MAUI limitation, say), update the mockup to
  what the code does, or add the difference to the list above.
- To explore something bigger, sketch it in the claude.ai design artifact first, then copy
  the chosen screens here.
