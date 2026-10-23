// Series the Russian catalog lacks get their Russian text, genres and cast from TheTVDB by exact IMDb id.
// Runs on the server with the project's licensed API key; the key and the session token never leave it.
import { castCredits, clean, readJson, UpstreamError, unique, yearOf, type TitleInfo } from "@/lib/title-info";

type Row = Record<string, unknown>;
const BASE = "https://api4.thetvdb.com/v4";
const rows = (value: unknown): Row[] => Array.isArray(value) ? value.filter((row): row is Row => !!row && typeof row === "object") : [];

// `short=true` omits characters in the live API. Keep the full extended record
// with translations; Russian text wins, then English, then the record's own.
export function parseSeries(imdbId: string, extended: Row): TitleInfo | null {
  const id = extended.id;
  if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0) return null;
  const translations = (extended.translations ?? {}) as Row;
  const pick = (list: unknown, field: string, language: string) => {
    const translated = rows(list).filter(row => row.language === language && clean(row[field], 4000));
    // The API also returns aliases in nameTranslations. Prefer its canonical
    // name regardless of their order, retaining an alias when it is all we have.
    return (translated.find(row => row.isAlias !== true) ?? translated[0])?.[field];
  };
  const original = clean(extended.name, 200);
  const title = clean(pick(translations.nameTranslations, "name", "rus"), 200) || original;
  if (!title) return null;
  const overview = clean(pick(translations.overviewTranslations, "overview", "rus") ?? pick(translations.overviewTranslations, "overview", "eng") ?? extended.overview, 4000);
  const characters = rows(extended.characters).filter(row => row.peopleType === "Actor" || row.type === 3).sort((a, b) => Number(a.sort ?? 999) - Number(b.sort ?? 999));
  const actors = characters.map(row => row.personName);
  return {
    imdbId, source: "TheTVDB", sourceId: id, section: "series", title, originalTitle: original && original !== title ? original : null,
    year: yearOf(extended.year ?? extended.firstAired), overview: overview || null,
    genres: unique(rows(extended.genres).map(row => row.name), 4), countries: unique([extended.originalCountry], 1),
    aliases: unique([title,original,...rows(translations.nameTranslations).filter(row=>row.language==="rus").map(row=>row.name),...rows(extended.aliases).filter(row=>row.language==="rus").map(row=>row.name)],16),
    actors: unique(actors, 12), directors: [],
    cast: castCredits(characters.map(row => ({ name: row.personName, character: row.name }))),
  };
}

// One session per key: the token lasts about a month, so it is renewed after 25 days or when TheTVDB refuses it.
export function tvdbSession(apiKey: string, request: typeof fetch = fetch, now = Date.now) {
  let token = "", expires = 0;
  async function login(signal: AbortSignal) {
    const answer = await readJson(`${BASE}/login`, { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ apikey: apiKey }), signal }, request);
    const value = answer?.data?.token;
    if (typeof value !== "string" || !value) throw new Error("TheTVDB did not return a session");
    token = value; expires = now() + 25 * 86400000;
  }
  return async function get(path: string, signal: AbortSignal): Promise<any> {
    if (!token || expires <= now()) await login(signal);
    const call = () => readJson(`${BASE}${path}`, { headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }, signal }, request, 2_000_000);
    try { return await call(); }
    catch (error) { if (!(error instanceof UpstreamError) || error.status !== 401) throw error; await login(signal); return call(); }
  };
}

export async function fetchSeries(imdbId: string, get: (path: string, signal: AbortSignal) => Promise<any>): Promise<TitleInfo | null> {
  if (!/^tt\d{5,10}$/.test(imdbId)) return null;
  const signal = AbortSignal.timeout(8000);
  let found: any;
  try { found = await get(`/search/remoteid/${imdbId}`, signal); }
  catch (error) { if (error instanceof UpstreamError && error.status === 404) return null; throw error; }
  const series = rows(found?.data).map(row => row.series).filter((row): row is Row => !!row && typeof row === "object");
  // One exact series only; an IMDb id that TheTVDB lists as a film or an episode is not a series.
  const id = series.length === 1 ? Number(series[0].id) : 0;
  if (!Number.isSafeInteger(id) || id <= 0) return null;
  const detail = await get(`/series/${id}/extended?meta=translations`, signal);
  return parseSeries(imdbId, (detail?.data ?? {}) as Row);
}
