export type Section = "movies" | "series";
export type Media = {
  id:string; section:Section; title:string; originalTitle?:string|null; year:number;
  poster?:string|null; pageUrl:string; description?:string|null;
  kinopoisk?:string|null; imdb?:string|null;
};

const ZONA="https://w6.zona.plus";
export const normalize=(value:string)=>value.toLocaleLowerCase("ru").replaceAll("ё","е").replace(/[^\p{L}\p{N}]+/gu," ").trim();
export function decodeHtml(value:string) {
  return value.replace(/&#(\d+);/g,(_,n)=>String.fromCodePoint(Number(n)))
    .replace(/&#x([0-9a-f]+);/gi,(_,n)=>String.fromCodePoint(parseInt(n,16)))
    .replace(/&(?:amp|quot|apos|lt|gt|nbsp);/g,match=>({"&amp;":"&","&quot;":"\"","&apos;":"'","&lt;":"<","&gt;":">","&nbsp;":" "})[match]??match);
}
const strip=(value:string)=>decodeHtml(value.replace(/<[^>]*>/g," ").replace(/\s+/g," ")).trim();
const attr=(html:string,name:string)=>new RegExp(`${name}=["']([^"']+)["']`,"i").exec(html)?.[1]??null;

export function parseCatalog(html:string,section:Section):Media[] {
  const expected=section==="movies"?"/movies/":"/tvseries/";
  const result:Media[]=[];
  for(const match of html.matchAll(/<li\s+class=["']results-item-wrap["'][^>]*>[\s\S]*?<\/li>/gi)) {
    const card=match[0];
    const path=attr(card.match(/<a\b[^>]*itemprop=["']url["'][^>]*>/i)?.[0]??"","href")??"";
    if(!path.startsWith(expected))continue;
    const rawTitle=/<[^>]+itemprop=["']name["'][^>]*>([\s\S]*?)<\//i.exec(card)?.[1]??"";
    const title=strip(rawTitle);
    if(!title)continue;
    const slug=path.slice(expected.length).split("/")[0];
    if(!/^[\p{L}\p{N}-]+$/u.test(slug))continue;
    const poster=attr(card.match(/<meta\b[^>]*itemprop=["']image["'][^>]*>/i)?.[0]??"","content");
    const year=Number(/class=["']results-item-year["'][^>]*>(\d{4})/i.exec(card)?.[1]??0);
    const rating=strip(/class=["']results-item-rating["'][^>]*>([\s\S]*?)<\/span>/i.exec(card)?.[1]??"");
    result.push({id:`${section}:${slug}`,section,title,year,poster:poster?.startsWith("https://")?poster:null,pageUrl:new URL(path,ZONA).href,kinopoisk:/^\d{1,2}([.,]\d)?$/.test(rating)?rating:null});
  }
  return result.slice(0,40);
}

export function parseDetail(html:string):Pick<Media,"originalTitle"|"description"|"kinopoisk"|"imdb"> {
  const original=attr(html.match(/<meta\b[^>]*itemprop=["']alternativeHeadline["'][^>]*>/i)?.[0]??"","content");
  const description=strip(/<[^>]+itemprop=["']description["'][^>]*>([\s\S]*?)<\/[^>]+>/i.exec(html)?.[1]??"");
  const score=(cls:string)=>strip(new RegExp(`class=["'][^"']*${cls}[^"']*["'][^>]*>([\\s\\S]*?)<\\/`,"i").exec(html)?.[1]??"");
  return {originalTitle:original?decodeHtml(original):null,description:description||null,kinopoisk:score("entity-rating-kp")||null,imdb:score("entity-rating-imdb")||null};
}

async function sourceText(url:string,timeout=8000) {
  const response=await fetch(url,{headers:{"Accept":"text/html,application/json;q=0.9,*/*;q=0.8","User-Agent":"KachalkaIndex/0.1"},signal:AbortSignal.timeout(timeout),cache:"no-store"});
  if(!response.ok)throw new Error(`Источник вернул ${response.status}`);
  const size=Number(response.headers.get("content-length")??0);
  if(size>4_000_000)throw new Error("Ответ источника слишком большой");
  const text=await response.text();if(text.length>4_000_000)throw new Error("Ответ источника слишком большой");
  return text;
}

export async function fetchCatalog(section:Section,query:string,page:number):Promise<Media[]> {
  const path=section==="movies"?"movies":"tvseries";
  const url=query?`${ZONA}/search-form?query=${encodeURIComponent(query)}`:`${ZONA}/${path}/filter/sort-date?page=${page}`;
  return parseCatalog(await sourceText(url),section);
}

export async function fetchDetail(media:Media) {
  const url=new URL(media.pageUrl);
  if(url.protocol!=="https:"||url.hostname!=="w6.zona.plus"||!url.pathname.startsWith(media.section==="movies"?"/movies/":"/tvseries/"))throw new Error("Неверный адрес карточки");
  return parseDetail(await sourceText(url.href));
}
