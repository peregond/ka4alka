// Films the Russian catalog lacks (found through IMDb search in the app) get Russian text, genres and credits
// from TMDB by exact IMDb id. Runs on the server; the token never leaves it.
import { castCredits, clean, readJson, unique, yearOf, type TitleInfo } from "@/lib/title-info";

type Row = Record<string, unknown>;
export function parseMovie(imdbId: string, detail: Row): TitleInfo | null {
  const id = detail.id;
  if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0) return null;
  const title = clean(detail.title, 200);
  if (!title) return null;
  const original = clean(detail.original_title, 200);
  const credits = (detail.credits ?? {}) as Row;
  const cast = Array.isArray(credits.cast) ? [...(credits.cast as Row[])].sort((a, b) => Number(a?.order ?? 999) - Number(b?.order ?? 999)) : [];
  const crew = Array.isArray(credits.crew) ? (credits.crew as Row[]).filter(row => row?.job === "Director") : [];
  const list = (rows: unknown, pick: (row: Row) => unknown = row => row.name) => Array.isArray(rows) ? rows.filter((row): row is Row => !!row && typeof row === "object").map(pick) : [];
  return {
    imdbId, source: "TMDB", sourceId: id, section: "movies", title, originalTitle: original || null, year: yearOf(detail.release_date),
    overview: clean(detail.overview, 4000) || null,
    genres: unique(list(detail.genres), 4).map(name => name.toLowerCase()), countries: unique(list(detail.production_countries), 3),
    actors: unique(list(cast), 12), directors: unique(list(crew), 2),
    cast: castCredits(cast.map(row => ({ name: row?.name, originalName: row?.original_name, character: row?.character }))),
    aliases: unique([title,original,...list(((detail.alternative_titles??{}) as Row).titles, row=>row.iso_3166_1==="RU"?row.title:null)],16),
  };
}

export async function fetchMovie(imdbId: string, token: string, request: typeof fetch = fetch): Promise<TitleInfo | null> {
  if (!token.trim()) throw new Error("TMDB is not configured");
  if (!/^tt\d{5,10}$/.test(imdbId)) return null;
  const signal = AbortSignal.timeout(7000);
  const read = (path: string) => readJson(`https://api.themoviedb.org/3/${path}`, { headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }, signal }, request);
  const found = await read(`find/${imdbId}?external_source=imdb_id&language=ru-RU`);
  const movies: Row[] = Array.isArray(found.movie_results) ? found.movie_results : [];
  // One exact answer only; an id that TMDB lists as something else is not a film.
  const id = movies.length === 1 ? movies[0].id : null;
  if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0) return null;
  return parseMovie(imdbId, await read(`movie/${id}?language=ru-RU&append_to_response=credits,alternative_titles`));
}
