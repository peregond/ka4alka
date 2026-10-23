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
  credits: { cast: [{ name: 'Зендея', order: 3 }, { name: 'Тимоти Шаламе', original_name: 'Timothée Chalamet', character: 'Paul Atreides', order: 0 }, { name: 'Тимоти Шаламе', order: 9 }], crew: [{ name: 'Дени Вильнёв', job: 'Director' }, { name: 'Кто-то', job: 'Writer' }] } });
assert.equal(film.cast[0].character, 'Paul Atreides'); assert.equal(film.cast[0].originalName, 'Timothée Chalamet');
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
// Public Solar Opposites fields observed on TheTVDB v4, 2026-10-11.
// Full extended records contain characters; short=true omits that field.
const extended = {
  "id": 375892,
  "name": "Solar Opposites",
  "year": "2020",
  "firstAired": "2020-05-08",
  "originalCountry": "usa",
  "overview": "A family of aliens move to middle America, where they debate whether life is better there or on their home planet.\r\n",
  "genres": [
    {
      "id": 2,
      "name": "Science Fiction"
    },
    {
      "id": 15,
      "name": "Comedy"
    },
    {
      "id": 17,
      "name": "Animation"
    }
  ],
  "characters": [
    {
      "peopleType": "Actor",
      "personName": "Sean Giambrone",
      "name": "Yumyulack",
      "sort": 3,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Justin Roiland",
      "sort": 7,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Mary Mack",
      "sort": 4,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Sagan McMahan",
      "sort": 5,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Andy Daly",
      "sort": 10,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Jason Mantzoukas",
      "sort": 9,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Christina Hendricks",
      "sort": 11,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Alfred Molina",
      "sort": 14,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Thomas Middleditch",
      "sort": 2,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Sterling K. Brown",
      "sort": 15,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Dan Stevens",
      "sort": 1,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Tiffany Haddish",
      "sort": 8,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Kieran Culkin",
      "sort": 19,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Kieran Culkin",
      "sort": 20,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Charlotte Nicdao",
      "sort": 16,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Clancy Brown",
      "sort": 17,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Carlos Alazraqui",
      "sort": 18,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Rob Schrab",
      "sort": 12,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Kari Wahlgren",
      "sort": 13,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Justin Roiland",
      "sort": 6,
      "type": 3
    },
    {
      "peopleType": "Actor",
      "personName": "Dan Bakkedahl",
      "sort": 21,
      "type": 3
    }
  ],
  "translations": {
    "nameTranslations": [
      {
        "name": "Solar Opposites",
        "language": "eng"
      },
      {
        "name": "Обратная сторона Земли",
        "language": "rus"
      },
      {
        "name": "Солнечные противоположности",
        "language": "rus",
        "isAlias": true
      }
    ],
    "overviewTranslations": [
      {
        "overview": "A family of aliens move to middle America, where they debate whether life is better there or on their home planet.\r\n",
        "language": "eng"
      },
      {
        "overview": "Семья инопланетян бежит со своей умирающей планеты и терпит крушение на Земле. Они обосновываются в тихом провинциальном городке в США и прилежно стараются адаптироваться к местным правилам и обычаям. Пытаясь стать своими среди чужих, пришельцы решают, годится ли Земля для проживания и последующего заселения, а пока главная их задача — заботиться о Пупе, живом тераформаторе, который очень любит сладкое, но однажды эволюционирует и захватит планету.",
        "language": "rus"
      }
    ]
  }
};
const show = tvdb.parseSeries('tt8910922', extended);
assert.equal(show.source, 'TheTVDB'); assert.equal(show.title, 'Обратная сторона Земли'); assert.equal(show.originalTitle, 'Solar Opposites'); assert.equal(show.year, 2020);
assert.equal(show.overview, extended.translations.overviewTranslations.find(row => row.language === 'rus').overview); same(show.genres, ['Science Fiction', 'Comedy', 'Animation']); same(show.countries, ['usa']);
same(show.actors, ['Dan Stevens', 'Thomas Middleditch', 'Sean Giambrone', 'Mary Mack', 'Sagan McMahan', 'Justin Roiland', 'Tiffany Haddish', 'Jason Mantzoukas', 'Andy Daly', 'Christina Hendricks', 'Rob Schrab', 'Kari Wahlgren']);
assert.equal(tvdb.parseSeries('tt8910922', { ...extended, characters: undefined }).actors.length, 0, 'short records have no cast');
const aliasesFirst = { ...extended, translations: { ...extended.translations, nameTranslations: [...extended.translations.nameTranslations].reverse() } };
assert.equal(tvdb.parseSeries('tt8910922', aliasesFirst).title, 'Обратная сторона Земли', 'canonical Russian title wins over an alias in either order');
assert.equal(tvdb.parseSeries('tt8910922', { ...extended, translations: { nameTranslations: [{ name: 'Солнечные противоположности', language: 'rus', isAlias: true }] } }).title, 'Солнечные противоположности', 'an alias remains a usable fallback');
const english = tvdb.parseSeries('tt8910922', { ...extended, translations: { overviewTranslations: [{ overview: 'Aliens fled.', language: 'eng' }] } });
assert.equal(english.title, 'Solar Opposites'); assert.equal(english.originalTitle, null); assert.equal(english.overview, 'Aliens fled.');
assert.equal(tvdb.parseSeries('tt8910922', { ...extended, translations: {} }).overview, 'A family of aliens move to middle America, where they debate whether life is better there or on their home planet.');
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
const get = tvdb.tvdbSession('fixture-key', tvdbFake({ data: [{ series: { id: 375892 } }] }), () => now);
const viaSearch = await tvdb.fetchSeries('tt8910922', get);
assert.equal(viaSearch.sourceId, 375892); assert(log[1].endsWith('/search/remoteid/tt8910922') && log[2].endsWith('/series/375892/extended?meta=translations'));
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

same(info.castCredits([{name:'Actor',character:'First'},{name:'Actor',character:'Second'},{name:'Actor',character:'First'},{name:'Unknown'}]).map(row=>row.character),['First / Second',null]);

assert.equal(tvdb.parseSeries("tt8910922",extended).cast.find(row=>row.name==="Sean Giambrone").character,"Yumyulack");
