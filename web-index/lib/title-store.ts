import { getIndexDb } from "@/db";
import type { TitleInfo, TitleSection } from "./title-info";

// Exact title identities survive Worker restarts. A failed refresh never deletes a useful record.
export async function readTitle(imdbId:string,section:TitleSection):Promise<TitleInfo|null> {
  const row=await getIndexDb().prepare("SELECT payload FROM title_metadata WHERE section=? AND imdb_id=? AND updated_at>? LIMIT 1").bind(section,imdbId,Date.now()-7*86400000).first<{payload:string}>();
  if(!row||row.payload.length>64000)return null;
  try {
    const value=JSON.parse(row.payload) as TitleInfo;
    return value.imdbId===imdbId&&value.section===section&&typeof value.title==="string"&&Number.isSafeInteger(value.sourceId)?value:null;
  } catch{return null;}
}
export async function writeTitle(item:TitleInfo) {
  await getIndexDb().prepare("INSERT INTO title_metadata(section,imdb_id,payload,updated_at) VALUES(?,?,?,?) ON CONFLICT(section,imdb_id) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at").bind(item.section,item.imdbId,JSON.stringify(item),Date.now()).run();
}
