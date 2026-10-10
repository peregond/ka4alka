import { decodeHtml, normalize, type Media } from "./catalog-source";
import type { Release } from "./release";

const text=(value:string)=>decodeHtml(value.replace(/<[^>]*>/g," ").replace(/\s+/g," ")).trim();
async function read(url:string,max=4_000_000) {
  const response=await fetch(url,{headers:{"Accept":"application/json,text/html,application/rss+xml,*/*","User-Agent":"KachalkaIndex/0.1"},signal:AbortSignal.timeout(8500),cache:"no-store"});
  if(!response.ok)throw new Error(`Источник вернул ${response.status}`);
  if(Number(response.headers.get("content-length")??0)>max)throw new Error("Ответ слишком большой");
  const body=await response.text();if(body.length>max)throw new Error("Ответ слишком большой");return body;
}
async function readWindows1251(url:string,max=2_000_000) {
  const response=await fetch(url,{headers:{"Accept":"text/html","User-Agent":"KachalkaIndex/0.3"},signal:AbortSignal.timeout(8500),cache:"no-store"});
  if(!response.ok||Number(response.headers.get("content-length")??0)>max)throw new Error("Источник недоступен");
  const bytes=await response.arrayBuffer();if(bytes.byteLength>max)throw new Error("Ответ слишком большой");
  return new TextDecoder("windows-1251").decode(bytes);
}
function encodeWindows1251(value:string) {
  const extra:Record<string,number>={"Ё":0xA8,"ё":0xB8,"І":0xB2,"і":0xB3,"Ї":0xAF,"ї":0xBF,"Є":0xAA,"є":0xBA,"Ґ":0xA5,"ґ":0xB4};
  return [...value].map(char=>{
    const code=char.codePointAt(0)??0;
    const byte=code>=0x410&&code<=0x44F?code-0x350:extra[char];
    return byte===undefined?encodeURIComponent(char):`%${byte.toString(16).toUpperCase().padStart(2,"0")}`;
  }).join("");
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
  return media.section==="series"||!["RuTor","NNM-Club","MegaPeer","BigFanGroup","The Pirate Bay","Knaben"].includes(source)||media.year===0||new RegExp(`(^|\\D)${media.year}(\\D|$)`).test(title);
}

export async function knaben(media:Media):Promise<Release[]> {
  const query=media.originalTitle||media.title;
  const response=await fetch("https://api.knaben.org/v1",{
    method:"POST",headers:{"Content-Type":"application/json","Accept":"application/json","User-Agent":"KachalkaIndex/0.2"},
    body:JSON.stringify({query,search_field:"title",search_type:"100%",order_by:"seeders",order_direction:"desc",size:75,hide_unsafe:true,hide_xxx:true}),
    signal:AbortSignal.timeout(9500),cache:"no-store"
  });
  if(!response.ok||Number(response.headers.get("content-length")??0)>2_000_000)throw new Error("Сводный индекс недоступен");
  const body=await response.text();if(body.length>2_000_000)throw new Error("Ответ сводного индекса слишком большой");
  const data=JSON.parse(body) as {hits?:{id?:string;title?:string;tracker?:string;category?:string;details?:string;link?:string;magnetUrl?:string;hash?:string;bytes?:number;seeders?:number}[]};
  const results:Release[]=[];
  for(const row of data.hits??[]) {
    if(!row.title||!row.id||!matches(media,row.title,"Knaben")||!/(?:movie|tv|anime|video|film|кино|сериал)/i.test(row.category??""))continue;
    const hash=/^[a-f0-9]{40}$/i.test(row.hash??"")?row.hash!.toUpperCase():hashFrom(row.magnetUrl??"");
    let download:string|null=hash?`magnet:?xt=urn:btih:${hash}&dn=${encodeURIComponent(row.title)}&tr=${encodeURIComponent("udp://tracker.opentrackr.org:1337/announce")}`:null;
    if(!download&&row.link){try{const url=new URL(row.link);if(url.protocol==="https:"&&["knaben.eu","knaben.org"].includes(url.hostname)&&url.pathname.startsWith("/live/dl/"))download=url.href;}catch{/* Ignore invalid links. */}}
    let page:string|null=null;
    if(row.details){try{const url=new URL(row.details);if(url.protocol==="https:")page=url.href;}catch{/* Ignore invalid links. */}}
    if(!download&&!page)continue;
    const source=(row.tracker||"Knaben").slice(0,40);
    results.push(make(media,`${source} · Knaben`,row.id,row.title,page,download,Number(row.bytes)||null,Number(row.seeders)||0));
  }
  return results;
}

export async function pirateBay(media:Media):Promise<Release[]> {
  const queries=[media.originalTitle,media.title].filter((value,index,all):value is string=>Boolean(value)&&all.indexOf(value)===index).slice(0,2);
  const category=media.section==="movies"?"201,202,207":"205,208";
  const responses=await Promise.allSettled(queries.map(async query=>JSON.parse(await read(`https://apibay.org/q.php?q=${encodeURIComponent(query)}&cat=${category}`,2_000_000)) as {id:string;name:string;info_hash:string;seeders:string;size:string;category:string}[]));
  const results:Release[]=[];
  for(const response of responses) {
    if(response.status!=="fulfilled"||!Array.isArray(response.value))continue;
    for(const row of response.value.slice(0,100)) {
      if(!/^\d+$/.test(row.id)||! /^[a-f0-9]{40}$/i.test(row.info_hash)||!matches(media,row.name,"The Pirate Bay"))continue;
      const hash=row.info_hash.toUpperCase();
      const magnet=`magnet:?xt=urn:btih:${hash}&dn=${encodeURIComponent(row.name)}&tr=${encodeURIComponent("udp://tracker.opentrackr.org:1337/announce")}`;
      results.push(make(media,"The Pirate Bay",hash,row.name,`https://thepiratebay.org/description.php?id=${row.id}`,magnet,Number(row.size)||null,Number(row.seeders)||0));
    }
  }
  return results;
}

export async function yts(media:Media):Promise<Release[]> {
  const query=media.originalTitle||media.title;
  if(media.section!=="movies"||!query||/[\u0400-\u04ff]/.test(query))return [];
  const response=JSON.parse(await read(`https://yts.gg/api/v2/list_movies.json?query_term=${encodeURIComponent(query)}&limit=50`,2_000_000)) as {status?:string;data?:{movies?:{title?:string;title_english?:string;year?:number;url?:string;torrents?:{hash?:string;quality?:string;type?:string;size_bytes?:number;seeds?:number}[]}[]}};
  if(response.status!=="ok")return [];
  const result:Release[]=[];
  for(const movie of response.data?.movies??[]) {
    if(![movie.title,movie.title_english].some(value=>value&&normalize(value)===normalize(query))||media.year&&Math.abs((movie.year??0)-media.year)>1)continue;
    const pageUrl=movie.url?.startsWith("https://yts.gg/movies/")?movie.url:null;
    for(const torrent of movie.torrents??[]) {
      if(!torrent.hash||! /^[a-f0-9]{40}$/i.test(torrent.hash))continue;
      const hash=torrent.hash.toUpperCase(),title=`${movie.title_english||movie.title} (${movie.year}) ${torrent.quality||""} ${torrent.type||""}`.trim();
      const magnet=`magnet:?xt=urn:btih:${hash}&dn=${encodeURIComponent(title)}&tr=${encodeURIComponent("udp://tracker.opentrackr.org:1337/announce")}`;
      result.push(make(media,"YTS",hash,title,pageUrl,magnet,Number(torrent.size_bytes)||null,Number(torrent.seeds)||0));
    }
  }
  return result;
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

export async function nnmClub(media:Media):Promise<Release[]> {
  const queries=[media.title,media.originalTitle].filter((value,index,all):value is string=>Boolean(value)&&all.indexOf(value)===index).slice(0,2);
  const groups=await Promise.allSettled(queries.map(query=>readWindows1251(`https://nnmclub.to/forum/tracker.php?nm=${encodeURIComponent(query)}`)));
  const results:Release[]=[];const seen=new Set<string>();
  for(const group of groups) {
    if(group.status!=="fulfilled")continue;
    for(const match of group.value.matchAll(/<tr\b[^>]*class=["'][^"']*\bprow[12]\b[^"']*["'][^>]*>([\s\S]*?)<\/tr>/gi)) {
      const row=match[1];
      const topic=/<a\b(?=[^>]*\bclass=["'][^"']*\btopictitle\b)[^>]*\bhref=["']viewtopic\.php\?t=(\d+)[^"']*["'][^>]*>([\s\S]*?)<\/a>/i.exec(row);
      const download=/href=["']download\.php\?id=(\d+)[^"']*["']/i.exec(row);
      if(!topic||!download||seen.has(download[1]))continue;
      const title=text(topic[2]);if(!matches(media,title,"NNM-Club"))continue;
      const size=Number(/<u>(\d{1,15})<\/u>/i.exec(row)?.[1]??0)||null;
      const seeds=Number(/<td\b[^>]*title=["']Seeders["'][^>]*>\s*(?:<b>)?\s*(\d+)/i.exec(row)?.[1]??0);
      seen.add(download[1]);
      results.push(make(media,"NNM-Club",download[1],title,`https://nnmclub.to/forum/viewtopic.php?t=${topic[1]}`,`https://nnmclub.to/forum/download.php?id=${download[1]}`,size,seeds));
      if(results.length>=80)return results;
    }
  }
  return results;
}

export async function megaPeer(media:Media):Promise<Release[]> {
  const queries=[media.title,media.originalTitle].filter((value,index,all):value is string=>Boolean(value)&&all.indexOf(value)===index).slice(0,2);
  const groups=await Promise.allSettled(queries.map(query=>readWindows1251(`https://megapeer.vip/browse.php?search=${encodeWindows1251(query)}&stype=0`)));
  const results:Release[]=[];const seen=new Set<string>();
  for(const group of groups) {
    if(group.status!=="fulfilled")continue;
    for(const match of group.value.matchAll(/<tr\b[^>]*class=["'][^"']*\btable_fon\b[^"']*["'][^>]*>([\s\S]*?)<\/tr>/gi)) {
      const row=match[1];
      const page=/<a\b[^>]*href=["'](\/torrent\/(\d+)\/[^"']+)["'][^>]*>([\s\S]*?)<\/a>/i.exec(row);
      const download=/href=["']\/download\/(\d+)["']/i.exec(row);
      if(!page||!download||page[2]!==download[1]||seen.has(download[1]))continue;
      const title=text(page[3]);if(!matches(media,title,"MegaPeer"))continue;
      const sizeMatch=/<td\b[^>]*align=["']right["'][^>]*>\s*(\d+(?:[.,]\d+)?)\s*(TB|GB|MB|KB|ТБ|ГБ|МБ|КБ)\s*<\/td>/i.exec(row);
      const units:Record<string,number>={TB:1024**4,GB:1024**3,MB:1024**2,KB:1024,ТБ:1024**4,ГБ:1024**3,МБ:1024**2,КБ:1024};
      const size=sizeMatch?Math.round(Number(sizeMatch[1].replace(",","."))*units[sizeMatch[2].toUpperCase()]):null;
      const seeds=Number(/alt=["']S["'][\s\S]*?<font\b[^>]*>\s*(\d+)\s*<\/font>/i.exec(row)?.[1]??0);
      seen.add(download[1]);
      results.push(make(media,"MegaPeer",download[1],title,`https://megapeer.vip${decodeHtml(page[1])}`,`https://megapeer.vip/download/${download[1]}`,size,seeds));
      if(results.length>=80)return results;
    }
  }
  return results;
}

const bigFanMovieCategories=new Set([13,14,15,18,19,20,21,22,23,24,26,27,28,29,30,31,33,36,39,47,48,51,52,53]);
export function parseBigFanGroup(html:string,media:Media):Release[] {
  const body=/<tbody\b[^>]*\bid=["']highlighted["'][^>]*>([\s\S]*?)<\/tbody>/i.exec(html)?.[1];
  if(!body)return [];
  const results:Release[]=[];const seen=new Set<string>();
  for(const match of body.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)) {
    const row=match[1],cells=[...row.matchAll(/<td\b[^>]*>([\s\S]*?)<\/td>/gi)].map(cell=>cell[1]);
    const link=/<a\b[^>]*href=["']details\.php\?id=([1-9]\d{0,11})["'][^>]*>([\s\S]*?)<\/a>/i.exec(row);
    const category=Number(/<a\b[^>]*href=["']browse\.php\?cat=(\d+)["']/i.exec(row)?.[1]??-1);
    if(!link||category<0||seen.has(link[1]))continue;
    // The tracker writes some season labels with a Latin "C".
    const title=text(link[2]).replace(/C(езон)/g,"С$1");
    const serial=category===11||/(?<![\p{L}\p{N}])(?:сезон|серии|s\d{1,2})/iu.test(title);
    if(media.section==="series"?!(serial&&(bigFanMovieCategories.has(category)||category===11||category===12)):serial||!bigFanMovieCategories.has(category))continue;
    if(!title||title.length>500||!matches(media,title,"BigFanGroup"))continue;
    const sizeMatch=/(\d+(?:[.,]\d+)?)\s*(TB|GB|MB|KB|ТБ|ГБ|МБ|КБ)/i.exec(text(cells[5]??""));
    const units:Record<string,number>={TB:1024**4,GB:1024**3,MB:1024**2,KB:1024,ТБ:1024**4,ГБ:1024**3,МБ:1024**2,КБ:1024};
    const size=sizeMatch?Math.floor(Number(sizeMatch[1].replace(",","."))*units[sizeMatch[2].toUpperCase()]):null;
    const seeds=/^\d{1,9}$/.test(text(cells[6]??""))?Number(text(cells[6]??"")):null;
    seen.add(link[1]);
    results.push(make(media,"BigFanGroup",link[1],title,`https://bigfangroup.org/details.php?id=${link[1]}`,`https://bigfangroup.org/download.php?id=${link[1]}`,size,seeds));
  }
  return results;
}

export async function bigFanGroup(media:Media):Promise<Release[]> {
  const queries=[media.title,media.originalTitle].filter((value,index,all):value is string=>Boolean(value)&&all.indexOf(value)===index).slice(0,2);
  const groups=await Promise.allSettled(queries.map(query=>readWindows1251(`https://bigfangroup.org/browse.php?ajax=1&search=${encodeWindows1251(query)}&cat=0&incldead=1&year=0&format=0&s=seed&d=desc`)));
  if(groups.every(group=>group.status==="rejected"))throw new Error("BigFanGroup недоступен");
  const unique=new Map<string,Release>();
  for(const group of groups)if(group.status==="fulfilled")for(const item of parseBigFanGroup(group.value,media))unique.set(item.id,item);
  return [...unique.values()].sort((a,b)=>(b.seeds??-1)-(a.seeds??-1)).slice(0,80);
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
  const sources=[rutor(media),nnmClub(media),megaPeer(media),bigFanGroup(media),pirateBay(media),knaben(media),archive(media),...(media.section==="series"?[eztv(media),nyaa(media)]:[yts(media)])];
  const groups=await Promise.allSettled(sources);const rows=groups.flatMap(group=>group.status==="fulfilled"?group.value:[]);
  const unique=new Map<string,Release>();
  for(const item of rows){const key=(hashFrom(item.torrentUrl??"")??item.pageUrl??item.id).toUpperCase();if(!unique.has(key))unique.set(key,item);}
  const bySource=new Map<string,Release[]>();
  for(const item of unique.values()){const list=bySource.get(item.source)??[];list.push(item);bySource.set(item.source,list);}
  const buckets=[...bySource.values()].map(list=>list.sort((a,b)=>(b.seeds??-1)-(a.seeds??-1)).slice(0,80));
  const balanced:Release[]=[];
  for(let index=0;balanced.length<200&&buckets.some(list=>list.length>index);index++)for(const list of buckets)if(list[index]&&balanced.length<200)balanced.push(list[index]);
  return balanced;
}
