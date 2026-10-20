# Mac: sidebar search and scroll chrome

Changes after the published 0.44.9, on `claude/quirky-hopper-1vlmj6`. Do not replace the published 0.44.9 packages with these binaries. Choose the next version after checking for newer work/releases.

## Behaviour

- Search is the first sidebar button and opens with Command-F from any page. It has one native `NSSearchField`, automatically focused after attaching to the real window. `AnyLayout` preserves that editor when the header switches between horizontal and vertical layouts.
- Return submits the query through the existing catalog engine. Scopes are Movies & series and Games. The eight recent queries are kept in memory, with a Clear action.
- Search is a navigation destination. Back from a result retains the search. Back out of Search restores the original section, filters, page, scroll anchor and loaded list. Search revisions do not invalidate the unchanged browse cache.
- The persistent native toolbar is hidden. The floating page title/actions appear after 56 points of scrolling and disappear below 8 points. Back remains available on detail pages and in the Search header.
- Scroll state lives in `ShellChromeView`, separate from the catalog. The overlay never changes the scroll viewport. One shared glass surface contains the toolbar buttons. The clip-view observer publishes threshold crossings and does not traverse the window during every scroll layout.
- Primary actions use capsules; secondary actions and icons use adaptive surfaces. Native Liquid Glass is compiled with supported SDKs and used on macOS 26 and later, including 27. Lite mode and Reduce Transparency use opaque surfaces. Poster/sidebar buttons retain their dedicated styles.
- Both Windows and Mac Featured pages start with the banner. The redundant page title, date and tagline are removed; individual collection headings remain.

## Validation and capture

The Windows changes passed the existing actual WPF UI/performance suite on two and four processor configurations: [run 39442263054](https://github.com/peregond/ka4alka/actions/runs/39442263054). The Windows files have not changed since that run.

Final application code: `6437e7adac96170635080a030e0fd5577a75dfe8`. All checks passed:

- [Native Mac run 39455348392](https://github.com/peregond/ka4alka/actions/runs/39455348392): Apple Silicon and Intel packages, 64 Swift tests on each architecture, actual-window matrices, shutdown, queue/deleted-file recovery, IPC, mutual TLS and signed updates. Compatibility jobs for macOS 15 / Xcode 16.4 and macOS 26 / Xcode 26 also passed.
- [macOS 27 run 39455348422](https://github.com/peregond/ka4alka/actions/runs/39455348422): macOS 27.0.1, 64 Swift tests, actual-window matrix and first-scroll performance checks. The matrix reported 169 checks, 116 combinations and zero violations.

The following handoff-only commit changes no application source or packages. The desktop version number remains 0.44.9 until the next release is prepared; no new desktop release was published for this work.

Mac validation runs exercise real packaged applications, both languages, themes, window sizes, native input focus, scroll chrome, first-scroll responsiveness, models and signed updates. The additional `macos-modern.yml` uses the official `xcode-27` image; the image used during this change runs macOS 27.0.1.

For review, download the run artifacts. Relevant captures are `search-1280x820-dark-ru.png`, `featured-1280x820-dark-ru.png`, their light English counterparts and `movies-scrolled-dark-ru.png`.

Smoke-only details worth preserving:

- Recognise both SwiftUI `accessibilityIdentifier` and AppKit `setAccessibilityIdentifier` in the source lint. The native search field sets `search-field` through AppKit.
- Read current native geometry for detail parents and children in the same coordinate system after resizing. Mixing a previous SwiftUI geometry proposal with fresh child positions causes false offscreen overflows.
- An empty search page intentionally has blank space below its controls. Its pixel audit samples the controls/empty-state region, with the original pixel thresholds intact.
- macOS 26 can return empty or incomplete cached GPU layers. Smoke capture uses the actual composited application window on that OS, flushes pending presentation, and extracts attached sheets using their real screen bounds. It does not capture other application windows or substitute generated images. This code never runs during a normal launch.

The changes do not update the live website, release gallery or published desktop packages by themselves. Publish a new, jointly verified Windows/Mac release when requested.
