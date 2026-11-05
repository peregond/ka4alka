import verified from "@/app/data/title-aliases.json";
import type { Media, Section } from "./catalog-source";

const normalized=(value:string)=>value.normalize("NFC").toLowerCase().replaceAll("ё","е").replace(/[^\p{L}\p{N}]+/gu," ").trim();
export function mediaNames(media:Media):string[] {
  return [...new Map([media.title,media.originalTitle,...(media.aliases??[])].filter((x):x is string=>typeof x==="string"&&x.trim().length>0&&x.length<=200).map(x=>[normalized(x),x.trim()])).values()].slice(0,16);
}
export function enrichMedia(media:Media):Media {
  const row=verified.find(x=>x.section===media.section&&(media.imdbId?x.imdbId===media.imdbId:media.year>0&&x.year===media.year&&x.aliases.some(name=>[media.title,media.originalTitle??""].some(title=>normalized(title)===normalized(name)))));
  if(!row)return media;
  return {...media,imdbId:media.imdbId??row.imdbId,originalTitle:media.originalTitle||row.originalTitle,aliases:mediaNames({...media,aliases:[...(media.aliases??[]),...row.aliases]})};
}
export function catalogQueries(section:Section,query:string):string[] {
  const row=verified.find(x=>x.section===section&&x.aliases.some(name=>normalized(name)===normalized(query)));
  return row?[...new Set([row.aliases[0],query,...row.aliases])]:[query];
}
