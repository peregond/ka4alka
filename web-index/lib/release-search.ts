import { fetchDetail, type Media, type Section } from "./catalog-source";
import type { Release } from "./release";
import { collectReleases } from "./release-sources";

// Release search without a database, for the standalone relay. The caller sends
// the catalog card it already shows; the answer has the shape of /api/releases.
export function searchMedia(params:URLSearchParams):Media|null {
  const match=/^(movies|series):([\p{L}\p{N}-]{1,120})$/u.exec(params.get("id")??"");
  const title=(params.get("title")??"").trim(),original=(params.get("original")??"").trim();
  const year=Number(params.get("year")??0);
  if(!match||!title||title.length>200||original.length>200||!Number.isInteger(year)||year<0||year>2100)return null;
  const section=match[1] as Section;
  return {id:match[0],section,title,originalTitle:original||null,year,pageUrl:`https://w6.zona.plus/${section==="movies"?"movies":"tvseries"}/${match[2]}`};
}

// The same source labels as asRelease in index-store.
export function presentRelease(item:Release):Release {
  const via=item.source.endsWith(" · Knaben")?"Knaben":null;
  const label=via?item.source.slice(0,-" · Knaben".length):item.source;
  return {...item,source:/^RuTracker(?:\.org)?$/i.test(label)?"RuTracker":label,via};
}

export async function searchReleases(request:Request):Promise<Response> {
  let media=searchMedia(new URL(request.url).searchParams);
  if(!media)return Response.json({error:"Неверная карточка."},{status:400});
  if(!media.originalTitle) {
    try{const detail=await fetchDetail(media);media={...media,originalTitle:detail.originalTitle??null};}catch{/* Search with the known title. */}
  }
  try {
    const items=(await collectReleases(media)).map(presentRelease);
    return Response.json({items,updated:true,sourceCount:new Set(items.map(x=>x.source)).size},{headers:{"Cache-Control":"no-store"}});
  } catch {return Response.json({error:"Варианты загрузки временно недоступны."},{status:503});}
}
