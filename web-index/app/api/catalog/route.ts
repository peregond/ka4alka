import { fetchCatalog, normalize } from "@/lib/catalog-source";
import { snapshotPage, PAGE_LIMIT } from "@/lib/catalog-snapshot";
import { lastSynced, markSynced, readMedia, upsertMedia, countMedia } from "@/lib/index-store";

export const runtime="edge";
export async function GET(request:Request) {
  const url=new URL(request.url),section=url.searchParams.get("section");
  if(section!=="movies"&&section!=="series")return Response.json({error:"Выбери фильмы или сериалы."},{status:400});
  const query=(url.searchParams.get("q")??"").trim().slice(0,80);
  const rawPage=Number(url.searchParams.get("page")??1);
  const page=query?1:Math.min(PAGE_LIMIT,Math.max(1,Number.isFinite(rawPage)?Math.floor(rawPage):1));
  const key=`catalog:${section}:${page}:${normalize(query)}`;
  const snapshot=snapshotPage(section,query,page);
  const respond=(items:typeof snapshot.items,sourceStatus:string,indexCount:number,hasMore:boolean)=>Response.json({items,sourceStatus,indexCount,hasMore,page},{headers:{"Cache-Control":"no-store"}});
  try {
    const cached=await readMedia(section,query,page);
    const fresh=Date.now()-await lastSynced(key)<15*60_000;
    if(!fresh||cached.length===0) {
      try {
        const found=await fetchCatalog(section,query,page);
        // Never mark an empty or blocked source response as a successful refresh.
        if(found.length) {
          await upsertMedia(found,query?null:page);await markSynced(key);
          const total=Math.max(await countMedia(section),snapshot.indexCount);
          return respond(found,"updated",total,!query&&found.length===40&&page<PAGE_LIMIT);
        }
      } catch { /* Serve the real versioned snapshot when the source is down. */ }
    } else if(cached.length===40||!snapshot.items.length) {
      const total=Math.max(await countMedia(section),snapshot.indexCount);
      return respond(cached,"indexed",total,!query&&page*40<total);
    }
    if(snapshot.items.length) {
      // Index the requested snapshot page as well, without delaying a request to crawl the whole source.
      try{await upsertMedia(snapshot.items,query?null:page);}catch{}
      return respond(snapshot.items,"snapshot",snapshot.indexCount,snapshot.hasMore&&!query);
    }
    const total=await countMedia(section);
    return respond(cached,"cached",total,!query&&page*40<total);
  } catch {
    // Missing D1 bindings must not reduce the bundled catalog to its first page.
    return respond(snapshot.items,"snapshot",snapshot.indexCount,snapshot.hasMore&&!query);
  }
}
