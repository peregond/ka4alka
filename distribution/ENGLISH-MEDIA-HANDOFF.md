# English media presentation

The desktop interface keeps canonical Russian catalog records and stable saved/download identities. English presentation is attached separately and never serialized into the shared catalog. Visible posters and the active banner share the existing bounded metadata scheduler; no whole-catalog fan-out is introduced.

- `GET /api/localized-media?language=en&id=series:yuzhnyi-park` accepts only trusted catalog identities. IMDb-only search cards use `imdb=tt0121955&section=series` instead of `id`.
- TMDB supplies English text, credits, an English poster (or text-free artwork), and a text-free/English backdrop. Exact IMDb identity, or unique exact title plus release year, is required. Missing translations/art keep a placeholder; Russian artwork is never silently labelled English.
- Keep the existing server-side `TMDB_READ_TOKEN` secret; no new credential or DB migration is required. English in-memory cache is bounded/coalesced; desktop disk cache is under `localization/en` and Steam detail caches include `ru`/`en` in the filename.
- Deploy the committed `web-index` source to the existing catalog service and Sites project. Existing D1/SQLite records, DB binding and secrets are preserved. Older Sites deployments lack the optional route; the desktop client tries its catalog mirrors without marking the entire site unavailable.
- `node tests/localized-media.mjs`, `node tests/tmdb-backdrop.mjs`, `node tests/title-lookup.mjs`, `node tests/title-identity.mjs`, and `tsc --noEmit` cover the server contract. Windows language/performance checks cover canonical identity, language switching, English release priority, separate backdrop caches and long game metadata at 700/1000/1280/1760 DIP.

Live TMDB and Railway verification on 2026-10-15: Dune (`tt1160419`, TMDB 438631) and South Park (`tt0121955`, TMDB 2190) returned English descriptions and image paths. The poster proxy returned JPEG successfully. Application release publication is a separate step.
