import { env } from "cloudflare:workers";
import seed from "@/app/data/seed.json";
import { getMedia } from "@/lib/index-store";
import { sharedCatalogItem } from "@/lib/shared-catalog";
import { fetchFranchise, franchiseCache } from "@/lib/movie-franchise";
import type { BackdropIdentity } from "@/lib/tmdb-backdrop";
export const runtime = "edge";
const lookup = franchiseCache(identity => fetchFranchise(identity, env.TMDB_READ_TOKEN ?? ""));
export async function GET(request: Request) {
  const params = new URL(request.url).searchParams;
  const id = params.get("id") ?? "", imdb = params.get("imdb") ?? "";
  const catalogRequest = /^movies:[\p{L}\p{N}-]{1,120}$/u.test(id);
  if (!catalogRequest && (id || !/^tt\d{5,10}$/.test(imdb))) return Response.json({ error: "Invalid movie identity" }, { status: 400 });
  if (!env.TMDB_READ_TOKEN) return Response.json({ error: "Metadata unavailable" }, { status: 503, headers: { "Cache-Control": "no-store" } });
  try {
    let identity: BackdropIdentity | null;
    if (catalogRequest) {
      const [stored, catalog] = await Promise.all([getMedia(id).catch(() => null), sharedCatalogItem(id).catch(() => null)]);
      const known = catalog ?? seed.find(row => row.id === id) ?? null;
      identity = stored ? { ...stored, imdbId: known?.imdbId ?? null, originalTitle: stored.originalTitle || known?.originalTitle } : known;
    } else identity = { section: "movies", imdbId: imdb, title: "", year: 0 };
    if (!identity || identity.section !== "movies") return Response.json({ error: "Movie not found" }, { status: 404 });
    const item = await lookup(id || imdb, { ...identity, language: params.get("language") === "en" ? "en" : "ru" });
    return Response.json({ id: id || imdb, item }, { headers: { "Cache-Control": `public, max-age=${item ? 86400 : 3600}` } });
  } catch { return Response.json({ error: "Metadata unavailable" }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "30" } }); }
}
