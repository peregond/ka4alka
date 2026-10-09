import { env } from "cloudflare:workers";
import seed from "@/app/data/seed.json";
import { getMedia } from "@/lib/index-store";
import { sharedCatalogItem } from "@/lib/shared-catalog";
import { backdropCache, fetchBackdrop, type BackdropIdentity } from "@/lib/tmdb-backdrop";

export const runtime = "edge";
const lookup = backdropCache(item => fetchBackdrop(item, env.TMDB_READ_TOKEN ?? ""));
export async function GET(request: Request) {
  const id = new URL(request.url).searchParams.get("id") ?? "";
  if (!/^(movies|series):[\p{L}\p{N}-]{1,120}$/u.test(id)) return Response.json({ error: "Неверная карточка." }, { status: 400 });
  if (!env.TMDB_READ_TOKEN) return Response.json({ error: "Источник фонов пока не подключён." }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "60" } });
  try {
    // Accept only identities from the stored catalog. Caller-supplied titles/URLs are never queried.
    const item = await getMedia(id).catch(() => null) ?? await sharedCatalogItem(id) ?? seed.find(row => row.id === id);
    if (!item) return Response.json({ error: "Карточка не найдена." }, { status: 404 });
    const backdrop = await lookup(id, item as BackdropIdentity);
    return Response.json({ id, backdrop }, { headers: { "Cache-Control": `public, max-age=${backdrop ? 86400 : 3600}, s-maxage=${backdrop ? 604800 : 21600}` } });
  } catch { return Response.json({ error: "Фон временно недоступен." }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "60" } }); }
}
