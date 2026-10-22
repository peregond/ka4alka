import { sharedCatalogPage } from "@/lib/shared-catalog";
import { fetchCatalog, normalize } from "@/lib/catalog-source";
import { catalogQueries, enrichMedia } from "@/lib/catalog-identity";
import { snapshotPage, PAGE_LIMIT } from "@/lib/catalog-snapshot";
import { lastSynced, markSynced, readMedia, upsertMedia, countMedia } from "@/lib/index-store";
import { completeCatalogSearch, mergeCatalogSearch } from "@/lib/catalog-search";

export const runtime="edge";
export async function GET(request:Request) {
  const url=new URL(request.url),section=url.searchParams.get("section");
  if(section!=="movies"&&section!=="series")return Response.json({error:"Выбери фильмы или сериалы."},{status:400});
  const query=(url.searchParams.get("q")??"").trim().normalize("NFC").slice(0,80);
  const rawPage=Number(url.searchParams.get("page")??1);
  const page=query?1:Math.min(PAGE_LIMIT,Math.max(1,Number.isFinite(rawPage)?Math.floor(rawPage):1));
  const key=`catalog:${section}:${page}:${normalize(query)}`;
  const queries=query?catalogQueries(section,query):[query];
  const sourceQuery=queries.find(name=>/[а-яё]/i.test(name))??query;
  const snapshot=snapshotPage(section,sourceQuery,page);
  const respond=(items:typeof snapshot.items,sourceStatus:string,indexCount:number,hasMore:boolean)=>Response.json({items:items.map(enrichMedia),sourceStatus,indexCount,hasMore,page},{headers:{"Cache-Control":"no-store"}});
  if(query) {
    // A recent-only shared library may contain a documentary or a sequel while
    // missing the original films. Query the source even when local matches exist.
    const [shared,cached,found]=await Promise.all([
      sharedCatalogPage(section,sourceQuery,1).catch(()=>null),
      readMedia(section,sourceQuery,1).catch(()=>[]),
      completeCatalogSearch(section,sourceQuery,()=>fetchCatalog(section,sourceQuery,1)),
    ]);
    const items=mergeCatalogSearch(section,query,shared?.items??[],snapshot.items,cached,found);
    try{await upsertMedia(items,null);if(found.length)await markSynced(key);}catch{}
    const total=Math.max(items.length,snapshot.indexCount,shared?.indexCount??0,await countMedia(section).catch(()=>0));
    return respond(items,found.length?"updated":cached.length?"indexed":"snapshot",total,false);
  }
  try {
    const shared=await sharedCatalogPage(section,sourceQuery,page);
    if(shared) {
      try{await upsertMedia(shared.items,query?null:page);}catch{}
      return Response.json({...shared,items:shared.items.map(enrichMedia),page,sourceStatus:"updated"},{headers:{"Cache-Control":"no-store"}});
    }
  } catch { /* The shared feed cannot replace existing offline fallbacks with an error. */ }
  try {
    const cached=await readMedia(section,sourceQuery,page);
    const fresh=Date.now()-await lastSynced(key)<15*60_000;
    if(!fresh||cached.length===0) {
      try {
        const found=await fetchCatalog(section,sourceQuery,page);
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
