# Handoff: verify TheTVDB v4 / TMDB title lookup against the live APIs

Status: **unverified.** The paths and fields in `web-index/lib/tvdb-title.ts` and
`web-index/lib/tmdb-title.ts` were written from documentation and third-party
clients, never against a real response. An attempt to verify them stopped because
the session had no `TVDB_API_KEY` / `TMDB_READ_TOKEN`. Nothing was changed in code.

## What to do

Needs `TVDB_API_KEY` (and `TMDB_READ_TOKEN` for the film check) in the environment.
Never print the keys, write them to files, logs, fixtures or commit messages.

1. `POST https://api4.thetvdb.com/v4/login` with `{"apikey": ...}`: where does the
   token sit in the response (assumed `data.token`)?
2. `GET /v4/search/remoteid/tt8910922` (Solar Opposites): response shape, is there a
   `series.id`?
3. `GET /v4/series/{id}/extended?meta=translations&short=true`: check `name`, `year`,
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
