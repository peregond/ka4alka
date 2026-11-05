import { env } from "cloudflare:workers";
import seed from "@/app/data/seed.json";
import { getMedia } from "@/lib/index-store";
import { sharedCatalogItem } from "@/lib/shared-catalog";
import { englishMediaCache, fetchEnglishMedia } from "@/lib/localized-media";
import type { BackdropIdentity } from "@/lib/tmdb-backdrop";
export const runtime = "edge";
const lookup = englishMediaCache(item => fetchEnglishMedia(item, env.TMDB_READ_TOKEN ?? ""));
const russianLookup = englishMediaCache(item => fetchEnglishMedia(item, env.TMDB_READ_TOKEN ?? "", fetch, "ru"));
export async function GET(request: Request) {
  const params = new URL(request.url).searchParams;
  const id = params.get("id") ?? "", imdb = params.get("imdb") ?? "", section = params.get("section");
  const catalogRequest = /^(movies|series):[\p{L}\p{N}-]{1,120}$/u.test(id);
  const language = params.get("language");
  if (language !== "en" && language !== "ru" || !catalogRequest && (id || !/^tt\d{5,10}$/.test(imdb) || section !== "movies" && section !== "series"))
    return Response.json({ error: "Invalid localization request" }, { status: 400 });
  if (!env.TMDB_READ_TOKEN) return Response.json({ error: "Metadata unavailable" }, { status: 503, headers: { "Cache-Control": "no-store" } });
  try {
    let identity: BackdropIdentity | null;
    if (catalogRequest) {
      const [stored, catalog] = await Promise.all([getMedia(id).catch(() => null), sharedCatalogItem(id).catch(() => null)]);
      const known = catalog ?? seed.find(row => row.id === id) ?? null;
      identity = stored ? { ...stored, imdbId: known?.imdbId ?? null, originalTitle: stored.originalTitle || known?.originalTitle } : known;
    } else identity = { section: section as "movies" | "series", imdbId: imdb, title: "", year: 0 };
    if (!identity) return Response.json({ error: "Title not found" }, { status: 404 });
    const item = await (language === "ru" ? russianLookup : lookup)(id || `${section}:${imdb}`, identity);
    return Response.json({ id: id || imdb, section: identity.section, item }, { headers: { "Cache-Control": `public, max-age=${item ? 86400 : 3600}` } });
  } catch { return Response.json({ error: "Metadata unavailable" }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "10" } }); }
}
