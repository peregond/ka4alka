import seed from "@/app/data/seed.json";
import { fetchCatalog, normalize, type Media, type Section } from "@/lib/catalog-source";
import { lastSynced, markSynced, readMedia, upsertMedia } from "@/lib/index-store";

export const runtime="edge";
const seeded=seed as Media[];
const fallback=(section:Section,query:string,page:number)=>{
  if(page>1)return [];
  const words=normalize(query).split(" ").filter(Boolean);
  return seeded.filter(x=>x.section===section&&words.every(word=>normalize(`${x.title} ${x.originalTitle??""} ${x.year}`).split(" ").some(part=>part.startsWith(word))));
};

export async function GET(request:Request) {
  const url=new URL(request.url),section=url.searchParams.get("section");
  if(section!=="movies"&&section!=="series")return Response.json({error:"Выбери фильмы или сериалы."},{status:400});
  const query=(url.searchParams.get("q")??"").trim().slice(0,80);
  const page=query?1:Math.min(40,Math.max(1,Number(url.searchParams.get("page")??1)||1));
  const key=`catalog:${section}:${page}:${normalize(query)}`;
  try {
    const cached=await readMedia(section,query,page);
    let updated=false,sourceError=false;
    if(cached.length===0||Date.now()-await lastSynced(key)>15*60_000) {
      try {
        const found=await fetchCatalog(section,query,page);
        if(found.length)await upsertMedia(found,query?null:page);
        await markSynced(key);updated=true;
      } catch {sourceError=true;}
    }
    const indexed=await readMedia(section,query,page);
    const items=indexed.length?indexed:fallback(section,query,page);
    return Response.json({items,sourceStatus:updated?"updated":sourceError?"cached":"indexed",indexCount:items.length},{headers:{"Cache-Control":"no-store"}});
  } catch {
    return Response.json({items:fallback(section,query,page),sourceStatus:"unavailable",error:"Онлайн-индекс временно недоступен."},{status:503,headers:{"Cache-Control":"no-store"}});
  }
}
