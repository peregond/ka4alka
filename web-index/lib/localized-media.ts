import { castCredits, clean, readJson, unique, type CastCredit } from "@/lib/title-info";
import { imageUrl, resolveTmdbId, selectBackdrop, type BackdropIdentity } from "@/lib/tmdb-backdrop";

export type EnglishMedia = {
  language: "en" | "ru"; title: string; description: string; poster: string | null; backdrop: string | null;
  genres: string[]; countries: string[]; actors: string[]; directors: string[];
  cast: CastCredit[];
};
// Identity comes from the trusted catalog, or an exact IMDb id. Never guess a remake from a fuzzy title.
export async function fetchEnglishMedia(item: BackdropIdentity, token: string, request: typeof fetch = fetch, language: "en" | "ru" = "en"): Promise<EnglishMedia | null> {
  if (!token.trim()) throw new Error("TMDB is not configured");
  const signal = AbortSignal.timeout(8000);
  const read = (path: string) => readJson(`https://api.themoviedb.org/3/${path}`, {
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }, signal,
  }, request);
  const id = await resolveTmdbId(item, read);
  if (!id) return null;
  const type = item.section === "movies" ? "movie" : "tv";
  const detail = await read(`${type}/${id}?language=${language === "en" ? "en-US" : "ru-RU"}&append_to_response=credits,images&include_image_language=${language},null`);
  if (detail.id !== id) throw new Error("Title identity mismatch");
  const title = clean(type === "movie" ? detail.title : detail.name, 300);
  if (!title) return null;
  const posters = (Array.isArray(detail.images?.posters) ? detail.images.posters : []) as Record<string, unknown>[];
  const candidates = posters.filter(row => imageUrl(row.file_path) && (row.iso_639_1 === language || row.iso_639_1 == null));
  candidates.sort((a, b) => Number(b.iso_639_1 === language) - Number(a.iso_639_1 === language) || Number(b.vote_count ?? 0) - Number(a.vote_count ?? 0));
  // Do not use the default poster_path: it can silently fall back to a Russian poster.
  const poster = imageUrl(candidates[0]?.file_path)?.replace("/w1280/", "/w500/") ?? null;
  const names = (rows: any, max: number) => unique(Array.isArray(rows) ? rows.map(row => row?.name) : [], max);
  const cast = Array.isArray(detail.credits?.cast) ? [...detail.credits.cast].sort((a, b) => Number(a.order ?? 999) - Number(b.order ?? 999)) : [];
  const directors = type === "movie" ? (detail.credits?.crew ?? []).filter((row: any) => row?.job === "Director") : detail.created_by;
  return { language, title, description: clean(detail.overview, 4000), poster,
    cast: castCredits(cast.map(row => ({ name: row?.name, originalName: row?.original_name, character: row?.character }))),
    backdrop: selectBackdrop(Array.isArray(detail.images?.backdrops) ? detail.images.backdrops : [], language),
    genres: names(detail.genres, 6), countries: names(detail.production_countries, 4), actors: names(cast, 12), directors: names(directors, 3) };
}

// Bounded/coalesced, separate from Russian title caches. Errors rest briefly and never cache an RU fallback as EN.
export function englishMediaCache(lookup: (item: BackdropIdentity) => Promise<EnglishMedia | null>, now = Date.now) {
  const saved = new Map<string, { expires: number; value: EnglishMedia | null }>();
  const pending = new Map<string, Promise<EnglishMedia | null>>(); let retryAfter = 0;
  return async (identity: string, item: BackdropIdentity) => {
    const key = JSON.stringify([identity, item.imdbId, item.title, item.originalTitle, item.year]);
    const cached = saved.get(key); if (cached && cached.expires > now()) return cached.value;
    const running = pending.get(key); if (running) return running;
    if (retryAfter > now() || pending.size >= 4) throw new Error("Localization is temporarily unavailable");
    const task = Promise.resolve().then(async () => {
      try { const value = await lookup(item); if (saved.size >= 512) saved.delete(saved.keys().next().value!);
        saved.set(key, { value, expires: now() + (value ? 7 * 86400000 : 6 * 3600000) }); return value;
      } catch { retryAfter = now() + 10000; throw new Error("Localization is temporarily unavailable"); }
      finally { pending.delete(key); }
    }); pending.set(key, task); return task;
  };
}
