# Native title, search and sidebar toggle — 0.44.14

- `ShellChromeView` uses the native `navigationTitle`. A custom principal Text
  item gained a Glass background on modern macOS and looked like a button.
- `SearchView` uses SwiftUI `searchable(placement: .toolbar)`,
  `searchPresentationToolbarBehavior(.avoidHidingContent)` and `searchFocused`
  on macOS 15+. A native NSSearchField in the toolbar preserves programmatic
  focus on macOS 14. There is no separate search-field header in page content.
- At window widths >= 1100, scope selection is a native toolbar segmented Picker.
  Narrower windows show a native compact menu below the toolbar: macOS otherwise
  moves supplementary toolbar controls into overflow beside the search field.
- `SearchToolbarLayoutObserver` uses actual NSWindow resize notifications, emitting
  only compact/wide transitions. SwiftUI content proposals could retain a stale
  width while AppKit resized a separate toolbar host. Do not use those proposals
  to decide whether a native toolbar item fits.
- RootView removes the automatic sidebar toggle in both the split view and its
  sidebar column. One icon-only automatic-style toolbar button remains.
- `AppModel.sidebarVisibility` is shared by that button, the split view binding,
  the View menu and Control-Command-S. Modern SwiftUI did not provide a responder
  target for NSSplitViewController.toggleSidebar on the test runner.
- The sidebar preserves a minimum width of 170 points after reopening.

Smoke checks post real pointer events, verify actual sidebar visibility and
compare the button size before/after. The sidebar container id is exactly
`sidebar`, not `sidebar.`; it must explicitly receive a native measurement probe.
An earlier false test failure measured a probe that was never installed. Search
checks cover focus, native editor buttons, titlebar placement and clipping; compact
scope remains mandatory. The pixel audit includes the actual search toolbar.

Final implementation source: `4ed8e198dc5420dbe4a1a79d66064eb4e34e6395`.
The compact native menu also records `menu.search.scope` for the generic menu
hit-area audit. Its layout target is at least 28 points high. The original Featured
scroll performance scenario runs before the separate sidebar interaction test;
its thresholds are unchanged.

Published as [0.44.14](https://github.com/peregond/ka4alka/releases/tag/v0.44.14)
on 2026-10-21 at 17:37:49 UTC (19:37 Europe/Belgrade), from the exact source above.

Validation passed:

- Release run `39641977747`: Windows, both Mac architectures, shared-engine,
  Mac package signing and public publisher verification.
- macOS 27 run `39641978244`: 550 real-window checks, 116 matrix cells with no
  violations, and 65 Swift tests.
- Apple Silicon release build: 550 real-window checks and 65 Swift tests.
- Intel release build: 549 real-window checks and 65 Swift tests.
- Focused layout run `39641983298`: 30 tests each on Intel, Apple Silicon and
  macOS 27, including menu hit areas, RTL and both interface languages.
- Actual Windows upgrade from published 0.44.13 preserved settings, downloaded
  7,384,495 of 107,736,931 bytes, and reused 422 files.

The release contains Windows setup/ZIP, both Mac DMG/ZIP pairs, signed update
manifests/component catalogs, SHA-256 checksums and platform-validation.json.
Public `latest.json` and `macos-latest.json` return HTTP 200 and version 0.44.14;
the public platform proof matches source `4ed8e19`.
