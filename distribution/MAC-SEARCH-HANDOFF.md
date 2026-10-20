# Mac: sidebar search and scroll chrome

The sidebar search and scroll chrome shipped in 0.44.10. The native window-control regression was corrected in published 0.44.11, source `4d12595ee7d43d41bd98c029025d919d30e300d9`, on `claude/quirky-hopper-1vlmj6`. Never replace an already published package.

## Behaviour

- Search is the first sidebar button and opens with Command-F from any page. It has one native `NSSearchField`, automatically focused after attaching to the real window. `AnyLayout` preserves that editor when the header switches between horizontal and vertical layouts.
- Return submits the query through the existing catalog engine. Scopes are Movies & series and Games. The eight recent queries are kept in memory, with a Clear action.
- Search is a navigation destination. Back from a result retains the search. Back out of Search restores the original section, filters, page, scroll anchor and loaded list. Search revisions do not invalidate the unchanged browse cache.
- Keep the native window toolbar visible, with an empty navigation title. Do not hide `NSToolbar` or use `.toolbar(.hidden, for: .windowToolbar)`: this hid the system traffic lights in 0.44.10. The floating page title/actions appear after 56 points of scrolling and disappear below 8 points. Back remains available on detail pages and in the Search header.
- Scroll state lives in `ShellChromeView`, separate from the catalog. The overlay never changes the scroll viewport. One shared glass surface contains the toolbar buttons. The clip-view observer publishes threshold crossings and does not traverse the window during every scroll layout.
- Primary actions use capsules; secondary actions and icons use adaptive surfaces. Native Liquid Glass is compiled with supported SDKs and used on macOS 26 and later, including 27. Lite mode and Reduce Transparency use opaque surfaces. Poster/sidebar buttons retain their dedicated styles.
- Both Windows and Mac Featured pages start with the banner. The redundant page title, date and tagline are removed; individual collection headings remain.

## Validation and capture

The Windows changes passed the existing actual WPF UI/performance suite on two and four processor configurations: [run 39442263054](https://github.com/peregond/ka4alka/actions/runs/39442263054). The Windows files have not changed since that run.

Final application code: `6437e7adac96170635080a030e0fd5577a75dfe8`. All checks passed:

- [Native Mac run 39455348392](https://github.com/peregond/ka4alka/actions/runs/39455348392): Apple Silicon and Intel packages, 64 Swift tests on each architecture, actual-window matrices, shutdown, queue/deleted-file recovery, IPC, mutual TLS and signed updates. Compatibility jobs for macOS 15 / Xcode 16.4 and macOS 26 / Xcode 26 also passed.
- [macOS 27 run 39455348422](https://github.com/peregond/ka4alka/actions/runs/39455348422): macOS 27.0.1, 64 Swift tests, actual-window matrix and first-scroll performance checks. The matrix reported 169 checks, 116 combinations and zero violations.

0.44.11 validation: [release run 39497766290](https://github.com/peregond/ka4alka/actions/runs/39497766290), [Mac compatibility run 39497734415](https://github.com/peregond/ka4alka/actions/runs/39497734415), and [macOS 27 run 39497734296](https://github.com/peregond/ka4alka/actions/runs/39497734296) passed. Actual-window audits check the traffic lights, and the close test clicks the real red button and verifies shutdown. AppKit normally disables the parent close button while an attached modal sheet is open; it must remain visible.

Mac validation runs exercise real packaged applications, both languages, themes, window sizes, native input focus, scroll chrome, first-scroll responsiveness, models and signed updates. The additional `macos-modern.yml` uses the official `xcode-27` image; the image used during this change runs macOS 27.0.1.

For review, download the run artifacts. Relevant captures are `search-1280x820-dark-ru.png`, `featured-1280x820-dark-ru.png`, their light English counterparts and `movies-scrolled-dark-ru.png`.

Smoke-only details worth preserving:

- Recognise both SwiftUI `accessibilityIdentifier` and AppKit `setAccessibilityIdentifier` in the source lint. The native search field sets `search-field` through AppKit.
- Read current native geometry for detail parents and children in the same coordinate system after resizing. Mixing a previous SwiftUI geometry proposal with fresh child positions causes false offscreen overflows.
- An empty search page intentionally has blank space below its controls. Its pixel audit samples the controls/empty-state region, with the original pixel thresholds intact.
- macOS 26 can return empty or incomplete cached GPU layers. Smoke capture uses the actual composited application window on that OS, flushes pending presentation, and extracts attached sheets using their real screen bounds. It does not capture other application windows or substitute generated images. This code never runs during a normal launch.

The desktop packages are published as 0.44.11. These source changes do not deploy the live website; screenshot workflows run separately after publication.
