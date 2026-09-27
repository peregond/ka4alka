import { decodeHtml, normalize, type Media } from "./catalog-source";
import type { Release } from "./index-store";

const text=(value:string)=>decodeHtml(value.replace(/<[^>]*>/g," ").replace(/\s+/g," ")).trim();
async function read(url:string,max=4_000_000) {
  const response=await fetch(url,{headers:{"Accept":"application/json,text/html,application/rss+xml,*/*","User-Agent":"KachalkaIndex/0.1"},signal:AbortSignal.timeout(8500),cache:"no-store"});
  if(!response.ok)throw new Error(`Источник вернул ${response.status}`);
  if(Number(response.headers.get("content-length")??0)>max)throw new Error("Ответ слишком большой");
  const body=await response.text();if(body.length>max)throw new Error("Ответ слишком большой");return body;
}
const hashFrom=(url:string)=>/urn:btih:([a-f0-9]{40})/i.exec(url)?.[1]?.toUpperCase()??null;
const quality=(title:string)=>/2160|\b4k\b|\buhd\b/i.test(title)?"2160p":/1080|full.?hd/i.test(title)?"1080p":/720|hdtv/i.test(title)?"720p":null;
const episode=(title:string)=>{const m=/\bS(\d{1,2})[. _-]*E(\d{1,3})\b/i.exec(title);if(m)return {season:Number(m[1]),episode:Number(m[2])};const s=/\b(?:season|сезон|сезона)\s*(\d{1,2})\b/i.exec(title);return {season:s?Number(s[1]):null,episode:null};};
const make=(media:Media,source:string,id:string,title:string,pageUrl:string|null,torrentUrl:string|null,size:number|null,seeds:number|null):Release=>({id:`${source}:${id}`,mediaId:media.id,title,source,pageUrl,torrentUrl,size,seeds,quality:quality(title),...episode(title)});

export function matches(media:Media,title:string,source:string) {
  const names=[media.title,media.originalTitle??""].map(normalize).filter(Boolean);
  const candidates=title.replace(/^(?:\s*\[[^\]]{1,40}\]\s*)+/,"").split(/\s+[\/|]\s+/).map(normalize);
  const hit=candidates.some(candidate=>names.some(name=>{
    if(candidate===name)return true;
    if(!candidate.startsWith(name+" "))return false;
    const tail=candidate.slice(name.length+1);
    if(names.some(other=>other!==name&&(tail===other||tail.startsWith(other+" "))))return true;
    if(media.section==="series"&&/^\d{2,4}\b/.test(tail))return true;
    return /^(?:s\d{1,2}(?:e\d{1,3})?|e\d{1,3}|season|сезон|серия|episode|complete|полный|все|web|hdtv|bdrip|bluray|remux|720p|1080p|2160p|4k|\d{4})\b/.test(tail);
  }));
  if(!hit)return false;
  return media.section==="series"||source!=="RuTor"||media.year===0||new RegExp(`(^|\\D)${media.year}(\\D|$)`).test(title);
}

export async function rutor(media:Media):Promise<Release[]> {
  const queries=[media.title,...(media.originalTitle&&media.originalTitle!==media.title?[media.originalTitle]:[])];
  const results:Release[]=[];
  for(const query of queries) {
    try {
      const html=await read(`https://rutor.info/search/0/0/000/0/${encodeURIComponent(query)}`);
      for(const row of html.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)) {
        const content=row[1],link=/<a\b[^>]*href=["'](\/torrent\/[^"']+)["'][^>]*>([\s\S]*?)<\/a>/i.exec(content);
        const magnet=/href=["'](magnet:\?xt=urn:btih:[^"']+)["']/i.exec(content)?.[1];
        if(!link||!magnet)continue;
        const title=text(link[2]);if(!matches(media,title,"RuTor"))continue;
        const cleanMagnet=decodeHtml(magnet);if(!cleanMagnet.startsWith("magnet:?xt=urn:btih:"))continue;
        const pageUrl=new URL(link[1],"https://rutor.info").href;
        const sizeMatch=/(\d+(?:[.,]\d+)?)\s*(GB|MB|ГБ|МБ)/i.exec(content.replace(/&nbsp;/g," "));
        const size=sizeMatch?Math.round(Number(sizeMatch[1].replace(",","."))*(sizeMatch[2].toUpperCase().startsWith("G")||sizeMatch[2]==="ГБ"?1073741824:1048576)):null;
        const seeds=Number(/class=["']green["'][^>]*>[\s\S]*?(\d+)\s*<\/span>/i.exec(content)?.[1]??0);
        results.push(make(media,"RuTor",hashFrom(cleanMagnet)??pageUrl,title,pageUrl,cleanMagnet,size,seeds));
      }
    } catch { /* Other sources remain available. */ }
  }
  return results;
}

export async function eztv(media:Media):Promise<Release[]> {
  const original=media.originalTitle;if(!original||/[\u0400-\u04ff]/i.test(original))return [];
  const search=JSON.parse(await read(`https://api.tvmaze.com/search/shows?q=${encodeURIComponent(original)}`,1_000_000)) as {show:{name:string;premiered?:string;externals?:{imdb?:string}}}[];
  const matched=search.filter(x=>normalize(x.show.name)===normalize(original)&&/^tt\d{6,12}$/.test(x.show.externals?.imdb??""))
    .sort((a,b)=>Math.abs(Number(a.show.premiered?.slice(0,4)??0)-media.year)-Math.abs(Number(b.show.premiered?.slice(0,4)??0)-media.year))[0];
  if(!matched||media.year&&Math.abs(Number(matched.show.premiered?.slice(0,4)??0)-media.year)>2)return [];
  const imdb=matched.show.externals!.imdb!.slice(2);const results:Release[]=[];
  for(let page=1;page<=2;page++) {
    const data=JSON.parse(await read(`https://eztvx.to/api/get-torrents?imdb_id=${imdb}&limit=100&page=${page}`)) as {torrents?:{title?:string;magnet_url?:string;hash?:string;size_bytes?:number|string;seeds?:number|string}[];torrents_count?:number};
    for(const row of data.torrents??[]) {
      const title=row.title??"",magnet=row.magnet_url??"";
      if(!matches(media,title,"EZTV")||!magnet.startsWith("magnet:?xt=urn:btih:"))continue;
      results.push(make(media,"EZTV",row.hash??hashFrom(magnet)??magnet,title,null,magnet,Number(row.size_bytes)||null,Number(row.seeds)||0));
    }
    if((data.torrents_count??0)<=100)break;
  }
  return results;
}

export async function nyaa(media:Media):Promise<Release[]> {
  if(media.section!=="series"||!media.originalTitle||/[\u0400-\u04ff]/i.test(media.originalTitle))return [];
  const xml=await read(`https://nyaa.si/?page=rss&c=1_0&f=0&q=${encodeURIComponent(media.originalTitle)}`,2_000_000);
  const results:Release[]=[];
  for(const item of xml.matchAll(/<item>([\s\S]*?)<\/item>/gi)) {
    const part=item[1],field=(tag:string)=>decodeHtml(new RegExp(`<${tag}[^>]*>([\\s\\S]*?)<\\/${tag}>`,"i").exec(part)?.[1]??"").replace(/^<!\[CDATA\[|\]\]>$/g,"").trim();
    const title=field("title"),link=field("link"),page=field("guid");
    if(!matches(media,title,"Nyaa"))continue;
    try { const url=new URL(link);if(url.protocol!=="https:"||url.hostname!=="nyaa.si"||!url.pathname.endsWith(".torrent"))continue; } catch {continue;}
    const sizeText=field("nyaa:size");const sizeMatch=/(\d+(?:\.\d+)?)\s*(KiB|MiB|GiB|TiB)/i.exec(sizeText);
    const size=sizeMatch?Math.round(Number(sizeMatch[1])*1024**({kib:1,mib:2,gib:3,tib:4}[sizeMatch[2].toLowerCase() as "kib"|"mib"|"gib"|"tib"])):null;
    const seeds=Number(field("nyaa:seeders"))||0;
    const hash=field("nyaa:infoHash")||page||link;
    results.push(make(media,"Nyaa",hash,title,page.startsWith("https://nyaa.si/")?page:null,link,size,seeds));
  }
  return results.slice(0,75);
}

export async function archive(media:Media):Promise<Release[]> {
  const query=(media.originalTitle||media.title).split(/\s+/).map(word=>`"${word.replace(/["\\]/g,"")}"`).join(" AND ");
  const url=`https://archive.org/advancedsearch.php?q=${encodeURIComponent(`mediatype:movies AND -noindex:true AND (${query})`)}&fl[]=identifier&fl[]=title&rows=24&page=1&sort[]=downloads+desc&output=json`;
  const data=JSON.parse(await read(url,2_000_000)) as {response?:{docs?:{identifier:string;title:string}[]}};
  return (data.response?.docs??[]).filter(x=>matches(media,x.title,"Internet Archive")).map(x=>make(media,"Internet Archive",x.identifier,x.title,`https://archive.org/details/${encodeURIComponent(x.identifier)}`,null,null,null));
}

export async function collectReleases(media:Media) {
  const sources=[rutor(media),archive(media),...(media.section==="series"?[eztv(media),nyaa(media)]:[])];
  const groups=await Promise.allSettled(sources);const rows=groups.flatMap(group=>group.status==="fulfilled"?group.value:[]);
  return [...new Map(rows.map(item=>[(hashFrom(item.torrentUrl??"")??item.pageUrl??item.id).toUpperCase(),item])).values()]
    .sort((a,b)=>(b.seeds??-1)-(a.seeds??-1)).slice(0,200);
}
