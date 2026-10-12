import { env } from "cloudflare:workers";
import { fetchMovie } from "@/lib/tmdb-title";
import { titleCache, type TitleSection } from "@/lib/title-info";
import { fetchSeries, tvdbSession } from "@/lib/tvdb-title";
import { readTitle, writeTitle } from "@/lib/title-store";

export const runtime = "edge";
// Series come from TheTVDB, films from TMDB. Each source has its own secret; a missing one only silences its section.
let session: { key: string; get: ReturnType<typeof tvdbSession> } | null = null;
const lookup = titleCache(async (imdbId, section) => {
  try{const stored=await readTitle(imdbId,section);if(stored)return stored;}catch{ /* Old deployments without the migration remain usable. */ }
  let item;
  if (section === "movies") item=await fetchMovie(imdbId, env.TMDB_READ_TOKEN ?? "");
  else {
    const key = env.TVDB_API_KEY ?? "";
    if (!session || session.key !== key) session = { key, get: tvdbSession(key) };
    item=await fetchSeries(imdbId, session.get);
  }
  if(item)try{await writeTitle(item);}catch{ /* Storage failure cannot discard a verified upstream answer. */ }
  return item;
});
export async function GET(request: Request) {
  const params = new URL(request.url).searchParams;
  const imdb = params.get("imdb") ?? "", section = params.get("section");
  // Only a well-formed IMDb id reaches an upstream; free text and addresses from the client are never queried.
  if (!/^tt\d{5,10}$/.test(imdb) || section !== "movies" && section !== "series") return Response.json({ error: "Неверный запрос." }, { status: 400 });
  if (!(section === "movies" ? env.TMDB_READ_TOKEN : env.TVDB_API_KEY)) return Response.json({ error: "Источник описаний пока не подключён." }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "60" } });
  try {
    const item = await lookup(imdb, section as TitleSection);
    if (!item) return Response.json({ error: "Название не найдено." }, { status: 404, headers: { "Cache-Control": "public, max-age=3600, s-maxage=21600" } });
    return Response.json({ item }, { headers: { "Cache-Control": "public, max-age=86400, s-maxage=604800" } });
  } catch { return Response.json({ error: "Описание временно недоступно." }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "60" } }); }
}
