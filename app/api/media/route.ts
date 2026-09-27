import seed from "@/app/data/seed.json";
import { fetchDetail, type Media } from "@/lib/catalog-source";
import { getMedia, updateMediaDetail, upsertMedia } from "@/lib/index-store";

export const runtime="edge";
export async function GET(request:Request) {
  const id=new URL(request.url).searchParams.get("id")??"";
  if(!/^(movies|series):[\p{L}\p{N}-]{1,120}$/u.test(id))return Response.json({error:"Неверная карточка."},{status:400});
  try {
    let item=await getMedia(id);
    if(!item){const found=(seed as Media[]).find(x=>x.id===id);if(found){await upsertMedia([found],null);item=found;}}
    if(!item)return Response.json({error:"Карточка не найдена."},{status:404});
    if(!item.originalTitle||!item.description) {
      try { const detail=await fetchDetail(item);await updateMediaDetail(id,detail);item={...item,...Object.fromEntries(Object.entries(detail).filter(([,value])=>value))}; } catch { /* Stored metadata remains usable. */ }
    }
    return Response.json({item},{headers:{"Cache-Control":"no-store"}});
  } catch {return Response.json({error:"Карточка временно недоступна."},{status:503});}
}
