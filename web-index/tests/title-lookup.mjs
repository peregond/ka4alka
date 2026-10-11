import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
function load(file, dependencies = {}, globals = {}) {
  const code = ts.transpileModule(fs.readFileSync(new URL(file, import.meta.url), 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true } }).outputText;
  const exports = {};
  vm.runInNewContext(code, { exports, require: name => dependencies[name], Response, Request, URL, URLSearchParams, AbortSignal, TextDecoder, Uint8Array, Promise, JSON, ...globals });
  return exports;
}
const same = (actual, expected) => assert.equal(JSON.stringify(actual), JSON.stringify(expected));
const info = load('../lib/title-info.ts');
const tmdb = load('../lib/tmdb-title.ts', { '@/lib/title-info': info });
const tvdb = load('../lib/tvdb-title.ts', { '@/lib/title-info': info });

// ----- TMDB: films -----
const film = tmdb.parseMovie('tt1160419', { id: 438631, title: ' Дюна ', original_title: 'Dune', release_date: '2021-09-15', overview: 'Пол  Атрейдес\nпопадает на Арракис.',
  genres: [{ name: 'Фантастика' }, { name: 'Приключения' }], production_countries: [{ name: 'США' }],
  credits: { cast: [{ name: 'Зендея', order: 3 }, { name: 'Тимоти Шаламе', order: 0 }, { name: 'Тимоти Шаламе', order: 9 }], crew: [{ name: 'Дени Вильнёв', job: 'Director' }, { name: 'Кто-то', job: 'Writer' }] } });
assert.equal(film.title, 'Дюна'); assert.equal(film.originalTitle, 'Dune'); assert.equal(film.year, 2021); assert.equal(film.source, 'TMDB');
assert.equal(film.overview, 'Пол Атрейдес попадает на Арракис.'); same(film.genres, ['фантастика', 'приключения']);
same(film.actors, ['Тимоти Шаламе', 'Зендея']); same(film.directors, ['Дени Вильнёв']);
assert.equal(tmdb.parseMovie('tt1', { id: 0, title: 'x' }), null); assert.equal(tmdb.parseMovie('tt1', { id: 5, title: ' ' }), null);
assert.equal(tmdb.parseMovie('tt1', { id: 5, title: 'X', release_date: '0000-01-01', overview: '' }).overview, null);
let calls = [];
const tmdbFake = found => async (url, options) => {
  assert.equal(options.headers.Authorization, 'Bearer fixture-token'); assert(!url.includes('fixture-token')); assert.equal(options.redirect, 'manual'); calls.push(url);
  return Response.json(url.includes('/find/') ? found : { id: 438631, title: 'Дюна' });
};
const viaFind = await tmdb.fetchMovie('tt1160419', 'fixture-token', tmdbFake({ movie_results: [{ id: 438631 }], tv_results: [] }));
assert.equal(viaFind.sourceId, 438631); assert(calls[0].includes('/find/tt1160419') && calls[0].includes('language=ru-RU') && calls[1].includes('/movie/438631?') && calls[1].includes('append_to_response=credits'));
calls = [];
assert.equal(await tmdb.fetchMovie('tt1160419', 'fixture-token', tmdbFake({ movie_results: [], tv_results: [{ id: 2 }] })), null); assert.equal(calls.length, 1);
assert.equal(await tmdb.fetchMovie('tt1160419', 'fixture-token', tmdbFake({ movie_results: [{ id: 1 }, { id: 2 }] })), null, 'an ambiguous answer is skipped');
assert.equal(await tmdb.fetchMovie('../x', 'fixture-token', tmdbFake({})), null);
await assert.rejects(tmdb.fetchMovie('tt1160419', ' ', tmdbFake({})), /not configured/);
await assert.rejects(tmdb.fetchMovie('tt1160419', 'fixture-token', async () => new Response('private-response', { status: 401 })), error => !error.message.includes('private-response'));
let redirects = 0;
await assert.rejects(tmdb.fetchMovie('tt1160419', 'fixture-token', async (url, options) => { redirects++; assert.equal(options.redirect, 'manual'); return new Response(null, { status: 302, headers: { Location: 'https://untrusted.example/' } }); }));
assert.equal(redirects, 1);
await assert.rejects(tmdb.fetchMovie('tt1160419', 'fixture-token', async () => new Response(' '.repeat(1_000_001))), /too large/);

// ----- TheTVDB: series -----
const extended = { id: 368010, name: 'Solar Opposites', year: '2020', originalCountry: 'usa', overview: 'English fallback.',
  genres: [{ name: 'Animation' }, { name: 'Comedy' }, { name: 'Science Fiction' }],
  characters: [{ personName: 'Thomas Middleditch', peopleType: 'Actor', sort: 2 }, { personName: 'Justin Roiland', peopleType: 'Actor', sort: 1 }, { personName: 'Justin Roiland', peopleType: 'Actor', sort: 7 }, { personName: 'Шоураннер', peopleType: 'Writer', sort: 0 }],
  translations: { nameTranslations: [{ name: 'Солнечные оппозиты', language: 'rus' }, { name: 'Solar Opposites', language: 'eng' }],
    overviewTranslations: [{ overview: 'Инопланетяне\n сбежали с  планеты.', language: 'rus' }, { overview: 'Aliens fled.', language: 'eng' }] } };
const show = tvdb.parseSeries('tt8910922', extended);
assert.equal(show.source, 'TheTVDB'); assert.equal(show.title, 'Солнечные оппозиты'); assert.equal(show.originalTitle, 'Solar Opposites'); assert.equal(show.year, 2020);
assert.equal(show.overview, 'Инопланетяне сбежали с планеты.'); same(show.genres, ['Animation', 'Comedy', 'Science Fiction']); same(show.countries, ['usa']);
same(show.actors, ['Justin Roiland', 'Thomas Middleditch']);
const english = tvdb.parseSeries('tt8910922', { ...extended, translations: { overviewTranslations: [{ overview: 'Aliens fled.', language: 'eng' }] } });
assert.equal(english.title, 'Solar Opposites'); assert.equal(english.originalTitle, null); assert.equal(english.overview, 'Aliens fled.');
assert.equal(tvdb.parseSeries('tt8910922', { ...extended, translations: {} }).overview, 'English fallback.');
assert.equal(tvdb.parseSeries('tt1', { id: 0, name: 'x' }), null); assert.equal(tvdb.parseSeries('tt1', { id: 5, name: ' ' }), null);

let log = [], logins = 0, rejectNext = false;
const tvdbFake = found => async (url, options) => {
  assert.equal(options.redirect, 'manual'); assert(!url.includes('fixture-key')); log.push(`${options.method ?? 'GET'} ${url}`);
  if (url.endsWith('/login')) { logins++; same(JSON.parse(options.body), { apikey: 'fixture-key' }); return Response.json({ status: 'success', data: { token: 'session-' + logins } }); }
  assert(options.headers.Authorization.startsWith('Bearer session-'));
  if (rejectNext) { rejectNext = false; return new Response('expired', { status: 401 }); }
  return Response.json(url.includes('/search/remoteid/') ? found : { data: extended });
};
let now = 0;
const get = tvdb.tvdbSession('fixture-key', tvdbFake({ data: [{ series: { id: 368010 } }] }), () => now);
const viaSearch = await tvdb.fetchSeries('tt8910922', get);
assert.equal(viaSearch.sourceId, 368010); assert(log[1].endsWith('/search/remoteid/tt8910922') && log[2].includes('/series/368010/extended?meta=translations&short=true'));
await tvdb.fetchSeries('tt8910922', get); assert.equal(logins, 1, 'the session is reused');
rejectNext = true; await tvdb.fetchSeries('tt8910922', get); assert.equal(logins, 2, 'a refused session is renewed once');
now = 26 * 86400000; await tvdb.fetchSeries('tt8910922', get); assert.equal(logins, 3, 'an old session is renewed');
const none = tvdb.tvdbSession('fixture-key', tvdbFake({ data: [{ movie: { id: 1 } }] }));
assert.equal(await tvdb.fetchSeries('tt8910922', none), null, 'an id that is a film is not a series');
assert.equal(await tvdb.fetchSeries('tt8910922', tvdb.tvdbSession('fixture-key', tvdbFake({ data: [{ series: { id: 1 } }, { series: { id: 2 } }] }))), null);
assert.equal(await tvdb.fetchSeries('tt8910922', async () => { throw new info.UpstreamError(404); }), null);
await assert.rejects(tvdb.fetchSeries('tt8910922', tvdb.tvdbSession('fixture-key', async () => new Response('private-response', { status: 403 }))), error => !error.message.includes('private-response'));
assert.equal(await tvdb.fetchSeries('x/../y', get), null);

// ----- cache -----
let time = 0, count = 0;
const cached = info.titleCache(async () => { count++; return show; }, () => time);
await Promise.all([cached('tt8910922', 'series'), cached('tt8910922', 'series')]); await cached('tt8910922', 'series'); assert.equal(count, 1);
await cached('tt8910922', 'movies'); assert.equal(count, 2, 'sections are cached apart');
time = 8 * 86400000; await cached('tt8910922', 'series'); assert.equal(count, 3);
let errors = 0; const failing = info.titleCache(async () => { errors++; throw new Error('secret'); }, () => time);
await assert.rejects(failing('tt8910922', 'series')); await assert.rejects(failing('tt8910922', 'series')); assert.equal(errors, 1);

// ----- route -----
const env = {}; let tmdbCalls = 0, tvdbCalls = 0;
const route = load('../app/api/title/route.ts', { 'cloudflare:workers': { env },
  '@/lib/tmdb-title': { fetchMovie: async () => { tmdbCalls++; return film; } }, '@/lib/title-info': { titleCache: lookup => lookup },
  '@/lib/tvdb-title': { tvdbSession: () => 'session', fetchSeries: async (id, session) => { tvdbCalls++; assert.equal(session, 'session'); return show; } } });
const req = query => new Request('https://example.test/api/title?' + query);
assert.equal((await route.GET(req('imdb=tt8910922%2F..&section=series'))).status, 400);
assert.equal((await route.GET(req('imdb=tt8910922'))).status, 400); assert.equal((await route.GET(req('imdb=tt8910922&section=books'))).status, 400);
assert.equal((await route.GET(req('imdb=tt8910922&section=series'))).status, 503); assert.equal((await route.GET(req('imdb=tt1160419&section=movies'))).status, 503);
env.TVDB_API_KEY = 'fixture-key';
const seriesAnswer = await route.GET(req('imdb=tt8910922&section=series')); assert.equal(seriesAnswer.status, 200); assert.equal((await seriesAnswer.json()).item.source, 'TheTVDB'); assert.equal(tvdbCalls, 1);
assert.equal((await route.GET(req('imdb=tt1160419&section=movies'))).status, 503, 'a film still needs the TMDB token'); assert.equal(tmdbCalls, 0);
env.TMDB_READ_TOKEN = 'fixture-token';
const filmAnswer = await route.GET(req('imdb=tt1160419&section=movies')); assert.equal((await filmAnswer.json()).item.source, 'TMDB'); assert.equal(tmdbCalls, 1);
console.log('PASS: IMDb title lookup — TheTVDB for series, TMDB for films, bounded and secret-free');
