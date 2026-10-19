# Remote release audio rules

From 0.44.2, both desktop frontends load the bounded JSON contract in `web-index/app/data/release-audio-rules.json`. The Railway endpoint `/api/release-audio-rules` serves the same file without DB access. The existing `catalog-data` GitHub Release carries a fallback copy. Sites has the same route when its verified source is published.

To update recognition without an application release, edit literal studio/type aliases, approved short tracker codes, or `dubbingPriority`; increment `revision`; run the audio regression suite. Push to `claude/quirky-hopper-1vlmj6`. `Publish release audio rules` tests the actual desktop parser and cache, then uploads only this data file and verifies its readback. Railway deploys the endpoint through its existing source connection. No app installer, update signature or component manifest is changed.

A card search starts rules refresh in parallel with source requests. Checks are limited to once per five minutes per client, and each network attempt has a two-second deadline. Fresh cached rules avoid a request; failed requests retain the last valid rules or the compiled fallback. Loading rules swaps the parsed-label cache atomically, so the same release objects pick up new aliases. Disk writes contain public rules only.

Schema 1 accepts at most 64 KiB, 100 studios, five known audio types and bounded literal aliases/prefixes/codes. It accepts neither scripts nor arbitrary remote regular expressions. Source page URLs, torrent hashes, sizes and peer counts remain source data; the rule file cannot invent a new release or bypass local quality/downloadability/minimum-seeder checks. The D code is recognized only in delimited metadata fields on known Russian sources.

Server search adapters already live in `web-index/lib/release-sources.ts` and can be deployed separately. The local BitTorrent engine remains a separately signed application component; this rule channel does not execute or install binaries.

Checks:

```
dotnet run --project desktop-core-tests/Kachalka.DesktopCoreTests.csproj -c Release -- --release-audio
cd web-index
node tests/release-audio-rules.mjs
```
