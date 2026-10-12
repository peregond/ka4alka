// Shared by the server's title lookups: the answer format, a bounded JSON reader and a small cache.
// Credentials stay in the callers; nothing here sees them except as request headers passed through.
export type TitleSection = "movies" | "series";
export type TitleInfo = {
  imdbId: string; source: "TheTVDB" | "TMDB"; sourceId: number; section: TitleSection; title: string; originalTitle: string | null; year: number;
  overview: string | null; genres: string[]; countries: string[]; actors: string[]; directors: string[];
  aliases?: string[];
};
export const clean = (value: unknown, max: number) => typeof value === "string" ? value.replace(/\s+/g, " ").trim().slice(0, max) : "";
export const unique = (values: unknown[], max: number) => [...new Set(values.map(value => clean(value, 120)).filter(Boolean))].slice(0, max);
export const yearOf = (value: unknown) => { const year = Number(clean(value, 10).slice(0, 4)); return Number.isInteger(year) && year >= 1880 && year <= 2100 ? year : 0; };

export class UpstreamError extends Error { constructor(readonly status: number) { super("Upstream is temporarily unavailable"); } }

// One size-bounded JSON read. Redirects are never followed, so a credential cannot be sent to another host,
// and the upstream body never appears in an error.
export async function readJson(url: string, init: RequestInit, request: typeof fetch, limit = 1_000_000): Promise<any> {
  const response = await request(url, { ...init, redirect: "manual" });
  if (!response.ok) throw new UpstreamError(response.status);
  if (Number(response.headers.get("content-length") ?? 0) > limit) throw new Error("Upstream response is too large");
  const reader = response.body?.getReader(); if (!reader) throw new Error("Empty upstream response");
  const chunks: Uint8Array[] = []; let length = 0;
  try {
    for (;;) { const next = await reader.read(); if (next.done) break; length += next.value.length; if (length > limit) throw new Error("Upstream response is too large"); chunks.push(next.value); }
  } finally { await reader.cancel(); }
  const bytes = new Uint8Array(length); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return JSON.parse(new TextDecoder().decode(bytes));
}

// Concurrent identical requests share one lookup. A match lasts a week, a miss six hours, a failure rests a minute.
export function titleCache(lookup: (imdbId: string, section: TitleSection) => Promise<TitleInfo | null>, now = Date.now) {
  const saved = new Map<string, { expires: number; value: TitleInfo | null }>();
  const pending = new Map<string, Promise<TitleInfo | null>>();
  const retryAfter = new Map<TitleSection,number>();
  return async (imdbId: string, section: TitleSection) => {
    const key = `${section}|${imdbId}`;
    const cached = saved.get(key); if (cached && cached.expires > now()) return cached.value;
    const running = pending.get(key); if (running) return running;
    if ((retryAfter.get(section)??0) > now() || pending.size >= 4) throw new Error("Title lookup is temporarily unavailable");
    const task = Promise.resolve().then(async () => {
      try {
        const value = await lookup(imdbId, section);
        if (saved.size >= 512) saved.delete(saved.keys().next().value!);
        saved.set(key, { value, expires: now() + (value ? 7 * 86400000 : 6 * 3600000) }); return value;
      } catch { retryAfter.set(section,now() + 60000); throw new Error("Title lookup is temporarily unavailable"); }
      finally { pending.delete(key); }
    });
    pending.set(key, task); return task;
  };
}
