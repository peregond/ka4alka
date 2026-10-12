import { env } from "cloudflare:workers";
import { fetchMovie } from "@/lib/tmdb-title";
import { titleCache, type TitleSection } from "@/lib/title-info";
import { fetchSeries, tvdbSession } from "@/lib/tvdb-title";

export const runtime = "edge";
// Series come from TheTVDB, films from TMDB. Each source has its own secret; a missing one only silences its section.
let session: { key: string; get: ReturnType<typeof tvdbSession> } | null = null;
const lookup = titleCache((imdbId, section) => {
  if (section === "movies") return fetchMovie(imdbId, env.TMDB_READ_TOKEN ?? "");
  const key = env.TVDB_API_KEY ?? "";
  if (!session || session.key !== key) session = { key, get: tvdbSession(key) };
  return fetchSeries(imdbId, session.get);
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
