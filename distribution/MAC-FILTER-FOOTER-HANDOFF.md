# Mac release filters and update footer

Prepared after published 0.44.12. Do not replace published 0.44.12 assets; choose a new version after checking current releases. Work stays on `claude/quirky-hopper-1vlmj6`.

- Release filters use the regular native control size. More Filters and Reset use `.bordered`, matching the native menu controls instead of mixing Glass with bordered menu buttons.
- Both filter rows use vertical centre alignment in `KDFlowLayout`. Other callers retain the previous top alignment. Row measurement and wrapping thresholds are unchanged.
- The ready update status no longer duplicates the restart button in the sidebar. Its complete message and component statistics remain in Settings → Updates. Progress and failures remain visible in the sidebar, including an error after a prepared update.
- Sidebar action/status/version have smoke-only layout probes. Their clipping must be checked against AppKit's current native visible rectangle; SwiftUI global coordinates and a requested window height are not a common coordinate space.

Validation source: `4baf0003c2ebe88320a70dc553b1d15e2637ec46`. [Targeted layout run 39577713717](https://github.com/peregond/ka4alka/actions/runs/39577713717) passed all three jobs: Intel / macOS 15, Apple Silicon / macOS 15, and macOS 27. Each job passed 30 tests, including ready-update action/version visibility at requested window heights 468, 520 and 700 in Russian and English. The real native intersection must expose at least 99% of each element.

The test respects system accessibility preferences; Reduce Transparency is a read-only environment value and must not be overridden with `.environment`. Full packaged-app and macOS 27 matrices were also triggered for this source: runs 39577635442 and 39577635501. Check their final status before publishing.
