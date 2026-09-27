import seed from "@/app/data/seed.json";
import { fetchDetail, type Media } from "@/lib/catalog-source";
import { collectReleases } from "@/lib/release-sources";
import { getMedia, lastSynced, markSynced, readReleases, updateMediaDetail, upsertMedia, upsertReleases } from "@/lib/index-store";

export const runtime="edge";
export async function GET(request:Request) {
  const id=new URL(request.url).searchParams.get("id")??"";
  if(!/^(movies|series):[\p{L}\p{N}-]{1,120}$/u.test(id))return Response.json({error:"Неверная карточка."},{status:400});
  try {
    let item=await getMedia(id);
    if(!item){const found=(seed as Media[]).find(x=>x.id===id);if(found){await upsertMedia([found],null);item=found;}}
    if(!item)return Response.json({error:"Карточка не найдена."},{status:404});
    const key=`releases:v3:${id}`;
    let updated=false;
    if(Date.now()-await lastSynced(key)>4*3600_000) {
      if(!item.originalTitle) {
        try {const detail=await fetchDetail(item);await updateMediaDetail(id,detail);item={...item,...Object.fromEntries(Object.entries(detail).filter(([,value])=>value))};} catch { /* Search with the known title. */ }
      }
      const found=await collectReleases(item);
      if(found.length)await upsertReleases(found);
      await markSynced(key);updated=true;
    }
    const releases=await readReleases(id);
    return Response.json({items:releases,updated,sourceCount:new Set(releases.map(x=>x.source)).size},{headers:{"Cache-Control":"no-store"}});
  } catch {return Response.json({error:"Варианты загрузки временно недоступны."},{status:503});}
}
