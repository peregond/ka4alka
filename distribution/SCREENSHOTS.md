# Platform galleries

The website uses the published Windows and macOS applications, rather than mockups. Its platform selector changes the screenshots, recommended download and installation steps together. Russian visitors see Russian captures; English and Spanish visitors see English captures. A manual language choice remains independent of the operating system.

The normal `publish-release.yml` publisher captures both platforms after publishing a verified release. For a release published separately, run `macos-screenshots.yml` and `screenshots.yml` on `claude/quirky-hopper-1vlmj6` with its exact version. Both jobs download and verify that release; they do not rebuild the application or replace its packages. Only screenshot assets are added to that release. Windows capture requires all visible source posters to load, so a source outage fails the capture instead of publishing an incomplete gallery.

The landing reads complete platform galleries from the latest public GitHub release. It refreshes release metadata every five minutes. If the new gallery is not ready, or an image cannot be fetched, it uses the checked-in gallery for the same operating system and labels its actual version. The current local Mac fallback is 0.44.1; the Windows fallback is 0.43.4. Capture metadata and image hashes are stored beside the Mac images.

- [Mac screenshots, Russian](assets/macos/0.44.1/ru/catalog-dark.webp)
- [Mac screenshots, English](assets/macos/0.44.1/en/catalog-dark.webp)
- [Mac capture provenance](assets/macos/0.44.1/capture-info.json)

Safari does not reliably disclose the Mac processor. The download control therefore offers Apple Silicon and Intel explicitly unless an explicit architecture client hint is available. iPads requesting desktop websites are not treated as Macs. The alternative operating system remains available.

`landing/tests/platform-browser.py` exercises the built landing on Windows, Mac, Apple Silicon hints, iPad, iPhone and Android. Its CI workflow stores browser screenshots as test evidence, separate from native application captures.
