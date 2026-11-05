# Native Mac window toolbar

The detached rounded overlay in `ShellChromeView` has been replaced by SwiftUI
window toolbar items. `RootView` applies it as a modifier rather than an overlay.

- Back uses the native navigation placement; it remains available on detail routes.
- The current page title uses the principal placement.
- Add torrent, refresh and appearance use the primary-action placement, with standard
  automatic button styling. AppKit controls the position, material and spacing.
- The page title and actions still appear after scrolling 56 points and disappear
  below 8 points, avoiding rebuilds on every scroll pixel.
- The system toolbar stays visible at all times to preserve traffic lights. The
  scroll observer no longer changes the window's title visibility or transparency.
- Search keeps its dedicated search field and receives no duplicate page actions.

The packaged smoke test now requires each action to be within 80 points of the
window's top edge, in addition to checking attachment and clipping. The mandatory
report patterns include the native-titlebar check. Existing checks cover hiding at
page top, showing after scrolling, returning to the top, and real window controls.

Implementation commit: `e86def391b1799db4a2e94e78f05bb765bede4f6`.
All checks passed for the exact implementation commit:

- Native packaged application: run `39581555524`, Intel and Apple Silicon;
  compatibility builds on macOS 15 / Xcode 16.4 and macOS 26 / Xcode 26.
- macOS 27 / Xcode 27: run `39581555422`, real window matrix and 65 Swift tests.
- Focused layout suite: run `39581555813`, 30 tests each on Intel, Apple Silicon
  and macOS 27.

The macOS 26 and macOS 27 `movies-scrolled-dark-ru.png` screenshots were visually
inspected: page title and actions are in the native top window chrome, with no
floating full-width capsule over the content. The changes were subsequently published in 0.44.13 (see below).


## Published 0.44.13

Published on 2026-10-21 at 11:17:04 UTC from
`e8240b48a5de1501351deb823ed3d16f258d9d57`:
https://github.com/peregond/ka4alka/releases/tag/v0.44.13

Release run `39587803520` passed Windows, both Mac architectures, shared-engine,
Mac asset signing and public publisher verification. Windows upgraded the actual
published 0.44.12 installation to 0.44.13, preserved settings, downloaded
7,384,511 of 107,736,947 bytes, and reused 422 files.

The release includes both Mac DMG and ZIP packages, Windows setup and ZIP,
signed update manifests/component catalogs, SHA-256 checksums and exact-source
platform validation. Public Windows and Mac update manifests return 0.44.13.

The user requested that future completed, successfully checked changes be
published without waiting for a separate “publish” instruction.
