import seed from "@/app/data/seed.json";
import type { Media, Section } from "./catalog-source";
import { catalogSearchScore } from "./catalog-search";
export const PAGE_SIZE=40, PAGE_LIMIT=250;
export function snapshotRows(section:Section,query=""):Media[] {
  return (seed as Media[]).filter(x=>x.section===section&&(!query.trim()||catalogSearchScore(x,query)>0));
}
export function snapshotPage(section:Section,query:string,page:number) {
  const rows=snapshotRows(section,query);
  const offset=query?0:(Math.max(1,page)-1)*PAGE_SIZE;
  return {items:rows.slice(offset,offset+(query?80:PAGE_SIZE)),indexCount:rows.length,hasMore:offset+PAGE_SIZE<rows.length};
}
