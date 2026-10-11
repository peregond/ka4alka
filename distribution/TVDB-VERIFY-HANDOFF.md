# Handoff: verify TheTVDB v4 / TMDB title lookup against the live APIs

Status: **TheTVDB verified live on 2026-10-11; TMDB blocked by HTTP 401.**

The actual `tvdbSession` / `fetchSeries` / `parseSeries` code was exercised with
Solar Opposites (`tt8910922`). Login returns `data.token`, remote search returns
`data: [{ series: { id: 375892, ... } }]`, and extended details return `data`.
The expected `name`, string `year`, `firstAired`, `overview`, `genres`,
`originalCountry`, and nested `translations` fields match. Translation language
codes are `rus` / `eng`. Russian name translations also contain an `isAlias`
entry; the parser now prefers the canonical name regardless of ordering.

**Correction:** `short=true` removes `characters`. The full
`/series/375892/extended?meta=translations` response has 21 character records with
`peopleType: "Actor"`, `personName`, numeric `sort`, and `type: 3`; its body was
50,492 bytes, below the existing 2 MB limit (the short response was 17,981 bytes).
`fetchSeries` now requests the full record. The test fixture uses a public field
subset from the live full record, including duplicate cast names and translations.

TMDB's exact Dune find request (`tt1160419`, `language=ru-RU`) returned HTTP 401
both through `fetchMovie` and an independent HTTP client. Movie details and their
field shapes remain unverified; no TMDB parser change was inferred from an
authentication failure. Live token expiry / renewal after a real 401 were not
forced; renewal and expiry behavior remain covered by fixture tests.

Validation: `node tests/title-lookup.mjs` and `tsc --noEmit` passed in an isolated
temporary copy using the existing installed dependencies. The corrected TVDB
lookup was then confirmed against the live API with Russian text and 12 unique,
sorted actors. Credentials and login responses were not retained in files.

## What to do

Needs `TVDB_API_KEY` (and `TMDB_READ_TOKEN` for the film check) in the environment.
Never print the keys, write them to files, logs, fixtures or commit messages.

1. `POST https://api4.thetvdb.com/v4/login` with `{"apikey": ...}`: where does the
   token sit in the response (assumed `data.token`)?
2. `GET /v4/search/remoteid/tt8910922` (Solar Opposites): response shape, is there a
   `series.id`?
3. `GET /v4/series/{id}/extended?meta=translations`: check `name`, `year`,
   `overview`, `genres`, `characters` (`peopleType`, `personName`, `sort`),
   `originalCountry`, `translations.nameTranslations` and `overviewTranslations`
   (`language` = `rus` / `eng`). Response must be under 2 MB.
4. Compare with `parseSeries`, `fetchSeries`, `tvdbSession` in
   `web-index/lib/tvdb-title.ts`. Fix paths/fields that differ, and adjust the fixture
   in `web-index/tests/title-lookup.mjs` to the real response shape (no key in it).
5. If `api.themoviedb.org` is reachable and `TMDB_READ_TOKEN` is set, check Dune
   (`tt1160419`) through `web-index/lib/tmdb-title.ts` the same way.
6. Run the tests: copy `web-index` to a temp dir, symlink `node_modules` to the global
   modules (e.g. `/opt/node22/lib/node_modules`), then `node tests/title-lookup.mjs`
   and `tsc --noEmit`.
7. Commit the fixes to this branch and push. Do not open a PR, do not touch `main`.

## Context

For titles missing from the Russian catalog (found via IMDb search) the app asks
`GET /api/title?imdb=tt…&section=series|movies`. Series come from TheTVDB API v4,
films from TMDB.

Files: `web-index/app/api/title/route.ts`, `web-index/lib/tvdb-title.ts`,
`web-index/lib/tmdb-title.ts`, `web-index/lib/title-info.ts`, `native/TitleMetadata.cs`,
`native/OnlineIndexClient.cs` (`Title`), `native/LiveWindow.cs` (`ImdbMetadata`),
tests `web-index/tests/title-lookup.mjs`, `tests/ImdbSuggestionTests.cs`,
description `distribution/title-descriptions.md`.
