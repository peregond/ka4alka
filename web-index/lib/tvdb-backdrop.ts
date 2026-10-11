// Server-only. The TheTVDB key never enters public JSON or image URLs.
import type { BackdropIdentity } from "./tmdb-backdrop";
export type TvdbBackdrop = { url: string; tvdbId: number; source: "TVDB" };
type Hit = { tvdb_id?: string | number; id?: string | number; name?: string; year?: string; aliases?: string[]; translations?: Record<string, string>; type?: string };
const normalize = (value: string) => value.normalize("NFKC").toLowerCase().replaceAll("ё", "е").replace(/[^\p{L}\p{N}]+/gu, " ").trim();
export function tvdbImageUrl(value: unknown): string | null {
  return typeof value === "string" && /^https:\/\/artworks\.thetvdb\.com\/banners\/[a-zA-Z0-9_\/-]+\.jpg$/.test(value) && !value.includes("..") ? value : null;
}
export function matchSeries(rows: Hit[], item: BackdropIdentity): number | null {
  // Exact title (localized, original, alias or translation) plus year; ambiguous results are skipped.
  if (!Number.isInteger(item.year) || item.year < 1880 || item.year > 2100) return null;
  const names = [item.title, item.originalTitle ?? ""].map(normalize).filter(Boolean);
  const matches = rows.filter(row => Number(row.year) === item.year && Number.isSafeInteger(Number(row.tvdb_id ?? row.id)) && Number(row.tvdb_id ?? row.id) > 0 &&
    [row.name, ...(row.aliases ?? []), ...Object.values(row.translations ?? {})].some(name => name && names.includes(normalize(name))));
  return matches.length === 1 ? Number(matches[0].tvdb_id ?? matches[0].id) : null;
}
export function selectTvdbBackdrop(rows: Record<string, unknown>[]): string | null {
  // TheTVDB artwork type 3 = series background. Prefer text-free, then Russian, then English; score breaks ties.
  const candidates = rows.filter(row => row.type === 3 && tvdbImageUrl(row.image) && typeof row.width === "number" && typeof row.height === "number" &&
    row.width >= 780 && row.height > 0 && row.width / row.height >= 1.6 && row.width / row.height <= 2.5);
  const rank = (row: Record<string, unknown>) => row.language == null ? 3 : row.language === "rus" ? 2 : row.language === "eng" ? 1 : 0;
  candidates.sort((a, b) => rank(b) - rank(a) || Number(b.score ?? 0) - Number(a.score ?? 0));
  return candidates.length ? tvdbImageUrl(candidates[0].image) : null;
}
async function readJson(response: Response) {
  if (!response.ok) throw new Error("TheTVDB is temporarily unavailable");
  if (Number(response.headers.get("content-length") ?? 0) > 1_000_000) throw new Error("TheTVDB response is too large");
  const reader = response.body?.getReader(); if (!reader) throw new Error("Empty TheTVDB response");
  const chunks: Uint8Array[] = []; let length = 0;
  try {
    for (;;) { const next = await reader.read(); if (next.done) break; length += next.value.length; if (length > 1_000_000) throw new Error("TheTVDB response is too large"); chunks.push(next.value); }
  } finally { await reader.cancel(); }
  const bytes = new Uint8Array(length); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return JSON.parse(new TextDecoder().decode(bytes));
}
export async function fetchTvdbBackdrop(item: BackdropIdentity, apiKey: string, request: typeof fetch = fetch): Promise<TvdbBackdrop | null> {
  if (!apiKey.trim()) throw new Error("TheTVDB is not configured");
  if (item.section !== "series") return null;
  const signal = AbortSignal.timeout(7000);
  const base = "https://api4.thetvdb.com/v4/";
  const login = await readJson(await request(`${base}login`, { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ apikey: apiKey }), signal, redirect: "manual" }));
  const token = login?.data?.token; if (typeof token !== "string" || !token) throw new Error("TheTVDB login failed");
  const read = async (path: string) => readJson(await request(base + path, { headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }, signal, redirect: "manual" }));
  let id: number | null = null;
  if (/^tt\d{7,10}$/.test(item.imdbId ?? "")) {
    const rows: { series?: { id?: number } }[] = (await read(`search/remote-id/${item.imdbId}`)).data ?? [];
    const series = rows.filter(row => row.series?.id);
    // An explicit identity never falls back to an unrelated title search.
    id = series.length === 1 && Number.isSafeInteger(series[0].series!.id) ? series[0].series!.id! : null;
  } else {
    for (const query of [...new Set([item.originalTitle, item.title].filter((name): name is string => !!name))]) {
      const result = await read(`search?query=${encodeURIComponent(query)}&type=series&year=${item.year}`);
      id = matchSeries(Array.isArray(result.data) ? result.data : [], item);
      if (id) break;
    }
  }
  if (!id) return null;
  const artworks = await read(`series/${id}/artworks?type=3`);
  const url = selectTvdbBackdrop(Array.isArray(artworks?.data?.artworks) ? artworks.data.artworks : []);
  return url ? { url, tvdbId: id, source: "TVDB" } : null;
}
