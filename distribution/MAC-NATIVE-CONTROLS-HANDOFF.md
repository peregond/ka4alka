# Native Mac controls

Published as [0.44.12](https://github.com/peregond/ka4alka/releases/tag/v0.44.12), source `df1682865aef97be86afbaf6b7675f21f151393e`, on `claude/quirky-hopper-1vlmj6`. Do not overwrite an existing release. Windows UI is unchanged.

## Implementation

- `KDPrimaryButtonStyle` and `KDSecondaryButtonStyle` are semantic `PrimitiveButtonStyle` adapters to SwiftUI's standard styles. They no longer draw control backgrounds, borders, hover/pressed effects or disabled opacity. Supported systems use `.glassProminent` / `.glass`; older systems, Lite, Reduce Transparency and Increase Contrast use `.borderedProminent` / `.bordered`.
- Icon buttons use native button styles and preserve destructive roles, hints and labels. Artwork/poster controls keep their separate artwork presentation.
- Filter chips have content-only labels; menus use `.borderedButton` and native indicators. Subtitle filtering uses a checkbox. Download destination menus no longer paint their own button backgrounds.
- Settings switches use a single labelled native `Toggle`, without a separate tap handler on their text.
- Search retains one focused `NSSearchField` through resizing, now with its native bezel, focus ring, search and cancel cells. The custom capsule and duplicate icons were removed. Cancel clears locally; only a nonempty submitted query starts a search. File search uses a standard bordered text field.
- Ordinary controls inherit the user's system accent. Semantic status and artwork colours remain separate.

## Distribution SDK

Both architecture jobs now explicitly select installed Xcode 26.3. The runner default was Xcode 16.4 / Swift 6.1.2, which compiled out every `#if compiler(>=6.2)` Liquid Glass branch. Keep the macOS 15 / Xcode 16.4 compatibility job to verify the fallback. Do not hide the native window toolbar: the traffic lights must remain visible, with only page actions following scrolling.

## Validation

Application changes: `08732fd27bafd47166a7460e6fac74aeb49f3b92`. Build SDK change: `d9e4dc94fd1e62d66d1a49fd3d7f53b8986ce9e0`.

- [macOS 27 run 39504189617](https://github.com/peregond/ka4alka/actions/runs/39504189617): 535 checks, 116 actual-window combinations, zero violations; all 64 Swift tests passed. Search native controls, input focus, traffic lights and scrolling are included.
- [Mac architecture/compatibility run 39504324398](https://github.com/peregond/ka4alka/actions/runs/39504324398), attempt 2: Apple Silicon and Intel built with Xcode 26.3; the actual-window matrices, native close, shared core, local pairing, IPC and all 64 Swift tests passed. macOS 26 / Xcode 26 and macOS 15 / Xcode 16.4 compatibility checks passed. The first attempt failed because Steam temporarily returned HTML instead of JSON; the same checks passed after the source recovered, without waiving UI requirements.

The macOS 27 runner has Reduce Transparency and Reduce Motion enabled. Its snapshots correctly show solid system controls; its environment report confirms the Glass branch was compiled. Relevant captures: `settings-1280x820-dark-ru.png`, `search-1280x820-dark-ru.png` and English/light equivalents.

## Published release verification

[Release run 39570239297](https://github.com/peregond/ka4alka/actions/runs/39570239297) passed Windows, both Mac packages built with Xcode 26.3, both shared-core jobs, signatures and public update checks. Each Mac package passed 535 actual-window checks and 64 Swift tests. The Windows 0.44.11 → 0.44.12 upgrade preserved settings, downloaded 7,379,082 bytes of a 107,731,518-byte package and reused 422 files. ZIP and DMG assets for both Mac architectures and the Windows installer are public. Screenshot workflows run separately after publication.
