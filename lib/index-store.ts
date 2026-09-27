import { getIndexDb } from "@/db";
import { normalize, type Media, type Section } from "./catalog-source";

export type Release = { id:string; mediaId:string; title:string; source:string; pageUrl:string|null; torrentUrl:string|null; size:number|null; seeds:number|null; quality:string|null; season:number|null; episode:number|null; indexedAt?:number };
type MediaRow = {id:string;section:Section;title:string;title_search:string;original_title:string|null;original_search:string;year:number;poster:string|null;page_url:string;description:string|null;kinopoisk:string|null;imdb:string|null;catalog_rank:number|null;indexed_at:number};
type ReleaseRow = {id:string;media_id:string;title:string;source:string;page_url:string|null;torrent_url:string|null;size:number|null;seeds:number|null;quality:string|null;season:number|null;episode:number|null;indexed_at:number};
const asMedia=(row:MediaRow):Media=>({id:row.id,section:row.section,title:row.title,originalTitle:row.original_title,year:row.year,poster:row.poster,pageUrl:row.page_url,description:row.description,kinopoisk:row.kinopoisk,imdb:row.imdb});
const asRelease=(row:ReleaseRow):Release=>({id:row.id,mediaId:row.media_id,title:row.title,source:row.source,pageUrl:row.page_url,torrentUrl:row.torrent_url,size:row.size,seeds:row.seeds,quality:row.quality,season:row.season,episode:row.episode,indexedAt:row.indexed_at});

export async function readMedia(section:Section,query:string,page=1):Promise<Media[]> {
  const db=getIndexDb();const term=normalize(query);
  if(term) {
    const result=await db.prepare("SELECT * FROM media WHERE section=? AND (title_search LIKE ? OR original_search LIKE ?) ORDER BY CASE WHEN title_search=? OR original_search=? THEN 0 WHEN title_search LIKE ? OR original_search LIKE ? THEN 1 ELSE 2 END, indexed_at DESC LIMIT 80")
      .bind(section,`%${term}%`,`%${term}%`,term,term,`${term}%`,`${term}%`).all<MediaRow>();
    return result.results.map(asMedia);
  }
  const result=await db.prepare("SELECT * FROM media WHERE section=? ORDER BY CASE WHEN catalog_rank IS NULL THEN 1 ELSE 0 END, catalog_rank ASC, indexed_at DESC LIMIT 40 OFFSET ?")
    .bind(section,(Math.max(1,page)-1)*40).all<MediaRow>();
  return result.results.map(asMedia);
}

export async function getMedia(id:string):Promise<Media|null> {
  const row=await getIndexDb().prepare("SELECT * FROM media WHERE id=? LIMIT 1").bind(id).first<MediaRow>();
  return row?asMedia(row):null;
}

export async function upsertMedia(items:Media[],page:number|null) {
  if(items.length===0)return;
  const db=getIndexDb(),now=Date.now();
  const sql="INSERT INTO media(id,section,title,title_search,original_title,original_search,year,poster,page_url,description,kinopoisk,imdb,catalog_rank,indexed_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET title=excluded.title,title_search=excluded.title_search,original_title=COALESCE(excluded.original_title,media.original_title),original_search=CASE WHEN excluded.original_title IS NULL THEN media.original_search ELSE excluded.original_search END,year=excluded.year,poster=COALESCE(excluded.poster,media.poster),description=COALESCE(excluded.description,media.description),kinopoisk=COALESCE(excluded.kinopoisk,media.kinopoisk),imdb=COALESCE(excluded.imdb,media.imdb),catalog_rank=COALESCE(excluded.catalog_rank,media.catalog_rank),indexed_at=excluded.indexed_at";
  const statements=items.slice(0,40).map((item,index)=>db.prepare(sql).bind(item.id,item.section,item.title,normalize(item.title),item.originalTitle??null,normalize(item.originalTitle??""),item.year||0,item.poster??null,item.pageUrl,item.description??null,item.kinopoisk??null,item.imdb??null,page===null?null:(page-1)*40+index,now));
  await db.batch(statements);
}

export async function updateMediaDetail(id:string,detail:Pick<Media,"originalTitle"|"description"|"kinopoisk"|"imdb">) {
  await getIndexDb().prepare("UPDATE media SET original_title=COALESCE(?,original_title),original_search=CASE WHEN ? IS NULL THEN original_search ELSE ? END,description=COALESCE(?,description),kinopoisk=COALESCE(?,kinopoisk),imdb=COALESCE(?,imdb) WHERE id=?")
    .bind(detail.originalTitle??null,detail.originalTitle??null,normalize(detail.originalTitle??""),detail.description??null,detail.kinopoisk??null,detail.imdb??null,id).run();
}

export async function lastSynced(key:string):Promise<number> {
  const row=await getIndexDb().prepare("SELECT refreshed_at FROM sync_state WHERE key=?").bind(key).first<{refreshed_at:number}>();
  return row?.refreshed_at??0;
}
export async function markSynced(key:string) {
  await getIndexDb().prepare("INSERT INTO sync_state(key,refreshed_at) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET refreshed_at=excluded.refreshed_at").bind(key,Date.now()).run();
}

export async function readReleases(mediaId:string):Promise<Release[]> {
  const result=await getIndexDb().prepare("SELECT * FROM releases WHERE media_id=? AND indexed_at>? ORDER BY CASE WHEN seeds IS NULL THEN 1 ELSE 0 END,seeds DESC,indexed_at DESC LIMIT 200")
    .bind(mediaId,Date.now()-7*24*3600*1000).all<ReleaseRow>();
  return result.results.map(asRelease);
}
export async function upsertReleases(items:Release[]) {
  if(items.length===0)return;
  const db=getIndexDb(),now=Date.now();
  const sql="INSERT INTO releases(id,media_id,title,source,page_url,torrent_url,size,seeds,quality,season,episode,indexed_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET seeds=COALESCE(excluded.seeds,releases.seeds),size=COALESCE(excluded.size,releases.size),indexed_at=excluded.indexed_at";
  for(let start=0;start<Math.min(items.length,200);start+=40) {
    const statements=items.slice(start,start+40).map(item=>db.prepare(sql).bind(item.id,item.mediaId,item.title,item.source,item.pageUrl,item.torrentUrl,item.size,item.seeds,item.quality,item.season,item.episode,now));
    await db.batch(statements);
  }
}
