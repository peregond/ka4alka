import { type Media, type Section } from "./catalog-source";

export function mergeCatalogPage(previous:Media[],incoming:Media[],section:Section,page:number,query:string) {
  const prior=new Map(previous.map(item=>[item.id,item]));
  const enriched=incoming.map(item=>({...prior.get(item.id),...item}));
  if(!query&&page===1)return [...enriched,...previous.filter(item=>item.section!==section)];
  const merged=new Map(prior);for(const item of enriched)merged.set(item.id,item);return [...merged.values()];
}
