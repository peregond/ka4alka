// This module runs on the server. Credentials never enter public JSON or image URLs.
export type BackdropIdentity = { section: "movies" | "series"; title: string; originalTitle?: string | null; year: number; imdbId?: string | null };
type Row = { id?: number; title?: string; original_title?: string; name?: string; original_name?: string; release_date?: string; first_air_date?: string; backdrop_path?: string };
export type Backdrop = { url: string; tmdbId: number; source: "TMDB" };
const normalize = (value: string) => value.normalize("NFKC").toLowerCase().replaceAll("ё", "е").replace(/[^\p{L}\p{N}]+/gu, " ").trim();
export function imageUrl(path: unknown): string | null {
  return typeof path === "string" && /^\/[a-zA-Z0-9_-]+\.jpg$/.test(path) ? `https://image.tmdb.org/t/p/w1280${path}` : null;
}
export function matchTitle(rows: Row[], item: BackdropIdentity): Row | null {
  // A year and an exact localized/original title are required. Ambiguous remakes are skipped.
  if (!Number.isInteger(item.year) || item.year < 1880 || item.year > 2100) return null;
  const names = [item.title, item.originalTitle ?? ""].map(normalize).filter(Boolean);
  const matches = rows.filter(row => Number.isSafeInteger(row.id) && (row.id ?? 0) > 0 &&
    Number((item.section === "movies" ? row.release_date : row.first_air_date)?.slice(0, 4)) === item.year &&
    [row.title, row.original_title, row.name, row.original_name].some(name => name && names.includes(normalize(name))));
  return matches.length === 1 ? matches[0] : null;
}
export function selectBackdrop(rows: Record<string, unknown>[]): string | null {
  const candidates = rows.filter(row => imageUrl(row.file_path) && typeof row.width === "number" && typeof row.height === "number" &&
    row.width >= 780 && row.height > 0 && row.width / row.height >= 1.6 && row.width / row.height <= 2.5);
  // Prefer text-free artwork, then Russian, then English; votes break ties.
  const languageRank = (row: Record<string, unknown>) => row.iso_639_1 == null ? 3 : row.iso_639_1 === "ru" ? 2 : row.iso_639_1 === "en" ? 1 : 0;
  candidates.sort((a, b) => languageRank(b) - languageRank(a) || Number(b.vote_count ?? 0) - Number(a.vote_count ?? 0));
  return candidates.length ? imageUrl(candidates[0].file_path) : null;
}
export async function fetchBackdrop(item: BackdropIdentity, token: string, request: typeof fetch = fetch): Promise<Backdrop | null> {
  if (!token.trim()) throw new Error("TMDB is not configured");
  const signal = AbortSignal.timeout(7000);
  async function read(path: string) {
    const response = await request(`https://api.themoviedb.org/3/${path}`, {
      headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }, signal, redirect: "error",
    });
    if (!response.ok) throw new Error("TMDB is temporarily unavailable");
    if (Number(response.headers.get("content-length") ?? 0) > 1_000_000) throw new Error("TMDB response is too large");
    const reader = response.body?.getReader(); if (!reader) throw new Error("Empty TMDB response");
    const chunks: Uint8Array[] = []; let length = 0;
    try {
      for (;;) { const next = await reader.read(); if (next.done) break; length += next.value.length; if (length > 1_000_000) throw new Error("TMDB response is too large"); chunks.push(next.value); }
    } finally { await reader.cancel(); }
    const bytes = new Uint8Array(length); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    return JSON.parse(new TextDecoder().decode(bytes));
  }
  const type = item.section === "movies" ? "movie" : "tv";
  let selected: Row | null = null;
  if (/^tt\d{7,10}$/.test(item.imdbId ?? "")) {
    const result = await read(`find/${item.imdbId}?external_source=imdb_id`);
    const rows: Row[] = result[type === "movie" ? "movie_results" : "tv_results"] ?? [];
    // An explicit identity never falls back to an unrelated title search.
    selected = rows.length === 1 && Number.isSafeInteger(rows[0].id) && (rows[0].id ?? 0) > 0 ? rows[0] : null;
  } else {
    for (const query of [...new Set([item.originalTitle, item.title].filter((name): name is string => !!name))]) {
      const result = await read(`search/${type}?query=${encodeURIComponent(query)}&language=ru-RU&include_adult=false&${type === "movie" ? "year" : "first_air_date_year"}=${item.year}`);
      selected = matchTitle(Array.isArray(result.results) ? result.results : [], item);
      if (selected) break;
    }
  }
  if (!selected) return null;
  const images = await read(`${type}/${selected.id}/images?include_image_language=null,ru,en`);
  const url = selectBackdrop(Array.isArray(images.backdrops) ? images.backdrops : []);
  return url ? { url, tmdbId: selected.id!, source: "TMDB" } : null;
}

// Coalesce concurrent banner requests. Successful matches last a week; no artwork lasts six hours.
export function backdropCache(lookup: (item: BackdropIdentity) => Promise<Backdrop | null>, now = Date.now) {
  const saved = new Map<string, { expires: number; value: Backdrop | null }>();
  const pending = new Map<string, Promise<Backdrop | null>>();
  let retryAfter = 0;
  return async (id: string, item: BackdropIdentity) => {
    const key = JSON.stringify([id, item.section, item.title, item.originalTitle, item.year, item.imdbId]);
    const cached = saved.get(key); if (cached && cached.expires > now()) return cached.value;
    const running = pending.get(key); if (running) return running;
    if (retryAfter > now() || pending.size >= 4) throw new Error("Backdrop lookup is temporarily unavailable");
    const task = Promise.resolve().then(async () => {
      try {
        const value = await lookup(item);
        if (saved.size >= 512) saved.delete(saved.keys().next().value!);
        saved.set(key, { value, expires: now() + (value ? 7 * 86400000 : 6 * 3600000) }); return value;
      } catch { retryAfter = now() + 60000; throw new Error("Backdrop lookup is temporarily unavailable"); }
      finally { pending.delete(key); }
    });
    pending.set(key, task); return task;
  };
}
