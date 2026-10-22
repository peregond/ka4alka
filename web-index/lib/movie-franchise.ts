import { clean, readJson, yearOf } from "@/lib/title-info";
import { imageUrl, resolveTmdbId, type BackdropIdentity } from "@/lib/tmdb-backdrop";
export type FranchisePart = { imdbId: string; title: string; originalTitle: string; year: number; description: string; poster: string | null; rating: string };
export type MovieFranchise = { id: number; name: string; items: FranchisePart[] };
// Collection membership is authoritative TMDB data, never a shared word in two titles.
export async function fetchFranchise(identity: BackdropIdentity, token: string, request: typeof fetch = fetch): Promise<MovieFranchise | null> {
  if (identity.section !== "movies") return null;
  if (!token.trim()) throw new Error("Metadata unavailable");
  const signal = AbortSignal.timeout(20000);
  const language = identity.language === "en" ? "en-US" : "ru-RU";
  const read = (path: string) => readJson(`https://api.themoviedb.org/3/${path}`, { signal, headers: { Authorization: `Bearer ${token}`, Accept: "application/json" } }, request);
  const movieId = await resolveTmdbId(identity, read);
  if (!movieId) return null;
  const suffix = `?language=${language}` + (identity.language === "en" ? "&append_to_response=images&include_image_language=en,null" : "");
  const movie = await read(`movie/${movieId}${suffix}`);
  if (movie.id !== movieId || identity.imdbId && movie.imdb_id !== identity.imdbId) throw new Error("Movie identity mismatch");
  const id = movie.belongs_to_collection?.id;
  if (!Number.isSafeInteger(id) || id <= 0) return null;
  const collection = await read(`collection/${id}?language=${language}`);
  if (collection.id !== id || !Array.isArray(collection.parts) || !collection.parts.some((part: any) => part.id === movieId)) throw new Error("Collection identity mismatch");
  const parts = [...new Set<number>(collection.parts.map((part: any) => part.id).filter((id: any) => Number.isSafeInteger(id) && id > 0))].slice(0, 40);
  const items: FranchisePart[] = []; let cursor = 0;
  async function worker() {
    for (;;) {
      const index = cursor++; if (index >= parts.length) return;
      const detail = parts[index] === movieId ? movie : await read(`movie/${parts[index]}${suffix}`);
      if (detail.id !== parts[index] || detail.belongs_to_collection?.id !== id) throw new Error("Part identity mismatch");
      if (!/^tt\d{5,10}$/.test(detail.imdb_id ?? "") || !clean(detail.title, 300)) continue;
      const score = Number(detail.vote_average);
      const posters = (Array.isArray(detail.images?.posters) ? detail.images.posters : []).filter((row: any) => (row.iso_639_1 === "en" || row.iso_639_1 == null) && imageUrl(row.file_path));
      posters.sort((a: any, b: any) => Number(b.iso_639_1 === "en") - Number(a.iso_639_1 === "en") || Number(b.vote_count ?? 0) - Number(a.vote_count ?? 0));
      const poster = imageUrl(identity.language === "en" ? posters[0]?.file_path : detail.poster_path)?.replace("/w1280/", "/w500/") ?? null;
      items.push({ imdbId: detail.imdb_id, title: clean(detail.title, 300), originalTitle: clean(detail.original_title, 300), year: yearOf(detail.release_date),
        description: clean(detail.overview, 4000), poster,
        rating: Number.isFinite(score) && score > 0 && score <= 10 ? score.toFixed(1) : "—" });
    }
  }
  await Promise.all(Array.from({ length: Math.min(3, parts.length) }, worker));
  items.sort((a, b) => (a.year || 9999) - (b.year || 9999) || a.imdbId.localeCompare(b.imdbId));
  return { id, name: clean(collection.name, 300), items };
}
export function franchiseCache(lookup: (identity: BackdropIdentity) => Promise<MovieFranchise | null>, now = Date.now) {
  const saved = new Map<string, { expires: number; value: MovieFranchise | null }>();
  const pending = new Map<string, Promise<MovieFranchise | null>>(); let retryAfter = 0;
  return async (key: string, identity: BackdropIdentity) => {
    key += `|${identity.language ?? "ru"}`;
    const previous = saved.get(key); if (previous && previous.expires > now()) return previous.value;
    const active = pending.get(key); if (active) return active;
    if (now() < retryAfter || pending.size >= 4) throw new Error("Metadata unavailable");
    const task = Promise.resolve().then(async () => {
      try { const value = await lookup(identity); if (saved.size >= 256) saved.delete(saved.keys().next().value!); saved.set(key, { value, expires: now() + (value ? 7 * 86400000 : 6 * 3600000) }); return value; }
      catch { retryAfter = now() + 30000; throw new Error("Metadata unavailable"); }
      finally { pending.delete(key); }
    }); pending.set(key, task); return task;
  };
}
