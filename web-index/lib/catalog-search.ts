import { normalize, type Media, type Section } from "./catalog-source";
import { enrichMedia, mediaNames } from "./catalog-identity";

export function catalogSearchScore(item:Media,query:string):number {
  const term=normalize(query),words=[...new Set(term.split(" ").filter(Boolean))].slice(0,12);
  if(!words.length)return 0;
  if(/^tt\d{5,10}$/.test(term))return enrichMedia(item).imdbId===term?2000:0;
  return Math.max(0,...mediaNames(enrichMedia(item)).map(name=>{
    const title=normalize(name),parts=title.split(" ");
    const matches=words.every(word=>{
      if(words.length>1&&/^(?:19|20)\d{2}$/.test(word)&&!parts.includes(word))return String(item.year)===word;
      return parts.some(part=>/^\d/.test(word)?part===word:part.startsWith(word));
    });
    return !matches?0:title===term?1000:title.startsWith(term)?800:600;
  }));
}

// Only an identical source ID is a merge decision. Similar names and remakes
// remain separate; a sparse source card cannot erase cached metadata.
export function mergeCatalogSearch(section:Section,query:string,...batches:Media[][]):Media[] {
  const rows=new Map<string,Media>();
  for(const batch of batches)for(const item of batch) {
    if(item.section!==section)continue;
    const fresh=enrichMedia(item),prior=rows.get(item.id);
    if(!prior){rows.set(item.id,fresh);continue;}
    const useful=Object.fromEntries(Object.entries(fresh).filter(([,value])=>value!==null&&value!==undefined&&value!==""&&(!Array.isArray(value)||value.length>0)));
    rows.set(item.id,{...prior,...useful,aliases:mediaNames({...prior,aliases:[...(prior.aliases??[]),...mediaNames(fresh)]})});
  }
  return [...rows.values()].map((item,index)=>({item,index,score:catalogSearchScore(item,query)})).sort((a,b)=>b.score-a.score||a.index-b.index).slice(0,80).map(row=>row.item);
}

// A cache entry stores the exact upstream response, rather than assuming that
// arbitrary matching rows in D1 constitute a complete answer to this query.
const results=new Map<string,{expires:number;items:Media[]}>();
const pending=new Map<string,Promise<Media[]>>();
export async function completeCatalogSearch(section:Section,query:string,read:()=>Promise<Media[]>):Promise<Media[]> {
  const key=section+":"+normalize(query),ready=results.get(key);
  if(ready&&ready.expires>Date.now())return ready.items;
  const active=pending.get(key);if(active)return active;
  if(pending.size>=8)return [];
  const work=(async()=>{
    let items:Media[]=[];
    try{items=(await read()).filter(item=>item.section===section).slice(0,60);}catch{ /* Local results remain usable. */ }
    if(results.size>=128)results.delete(results.keys().next().value!);
    results.set(key,{expires:Date.now()+(items.length?15*60_000:30_000),items});
    return items;
  })().finally(()=>pending.delete(key));
  pending.set(key,work);return work;
}
