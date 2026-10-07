import { normalize, type Media, type Section } from "./catalog-source";

const FEED="https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog.json";
type SharedIndex={items:Media[];generatedAtUtc:string};
let cached:SharedIndex|null=null;
let retryAfter=0;
let checking:Promise<SharedIndex|null>|null=null;
export function refreshBoundary(now=Date.now()) {
  const date=new Date(now);date.setUTCHours(5,0,0,0);if(date.getTime()>now)date.setUTCDate(date.getUTCDate()-1);return date.getTime();
}
export function parseSharedCatalog(value:unknown,now=Date.now()) {
  const feed=value as {schemaVersion?:number;generatedAtUtc?:string;items?:Media[]};
  const time=Date.parse(feed?.generatedAtUtc??"");
  if(feed?.schemaVersion!==1||!Number.isFinite(time)||time>now+15*60_000||time<Date.UTC(2020,0,1)||!Array.isArray(feed.items)||feed.items.length>15000)throw new Error("Invalid shared catalog");
  const unique=new Map<string,Media>();
  for(const row of feed.items) {
    if(!row||row.section!=="movies"&&row.section!=="series"||typeof row.title!=="string"||!row.title.trim()||!Number.isInteger(row.year)||row.year<0||row.year>2100)continue;
    try {
      const url=new URL(row.pageUrl),prefix=row.section==="movies"?"/movies/":"/tvseries/";
      const slug=decodeURIComponent(url.pathname.slice(prefix.length).replace(/\/$/,""));
      if(url.protocol!=="https:"||url.hostname!=="w6.zona.plus"||url.port||url.username||url.password||url.search||url.hash||!url.pathname.startsWith(prefix)||!/^[-\p{L}\p{N}]{1,120}$/u.test(slug)||row.id!==`${row.section}:${slug}`)continue;
      unique.set(row.id,row);
    } catch { /* Ignore malformed records rather than exposing unsafe links. */ }
  }
  const items=[...unique.values()];
  if(items.filter(x=>x.section==="movies").length<2000||items.filter(x=>x.section==="series").length<300)throw new Error("Incomplete shared catalog");
  items.sort((a,b)=>Math.min(b.year,new Date(now).getUTCFullYear())-Math.min(a.year,new Date(now).getUTCFullYear()));
  return {items,generatedAtUtc:feed.generatedAtUtc!};
}
export async function sharedCatalogPage(section:Section,query:string,page:number) {
  if(checking)await checking;
  const now=Date.now();
  if((!cached||Date.parse(cached.generatedAtUtc)<refreshBoundary(now))&&now>=retryAfter) {
    if(!checking)checking=(async()=>{
      retryAfter=Date.now()+30*60_000;
      try {
        const response=await fetch(FEED,{signal:AbortSignal.timeout(6000),cache:"no-store"});
        if(!response.ok||Number(response.headers.get("content-length"))>4*1024*1024)throw new Error("Catalog unavailable");
        const bytes=await response.arrayBuffer();if(bytes.byteLength>4*1024*1024)throw new Error("Oversized catalog");
        const fresh=parseSharedCatalog(JSON.parse(new TextDecoder().decode(bytes)));
        if(!cached||Date.parse(fresh.generatedAtUtc)>=Date.parse(cached.generatedAtUtc))cached=fresh;
      } catch { /* Preserve the previous complete catalog on a failed daily refresh. */ }
      return cached;
    })().finally(()=>{checking=null});
    await checking;
  }
  if(!cached)return null;
  const words=normalize(query).split(" ").filter(Boolean);
  const all=cached.items.filter(item=>item.section===section);
  const rows=all.filter(item=>words.every(word=>normalize(`${item.title} ${item.originalTitle??""} ${item.year}`).split(" ").some(part=>part.startsWith(word))));
  if(query&&rows.length===0)return null;
  const offset=query?0:(page-1)*40;
  return {items:rows.slice(offset,offset+(query?80:40)),indexCount:all.length,hasMore:!query&&offset+40<rows.length,updatedAt:cached.generatedAtUtc};
}
