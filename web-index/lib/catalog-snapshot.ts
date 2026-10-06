import seed from "@/app/data/seed.json";
import { normalize, type Media, type Section } from "./catalog-source";
export const PAGE_SIZE=40, PAGE_LIMIT=250;
export function snapshotRows(section:Section,query=""):Media[] {
  const words=normalize(query).split(" ").filter(Boolean);
  return (seed as Media[]).filter(x=>x.section===section&&words.every(word=>normalize(`${x.title} ${x.originalTitle??""} ${x.year}`).split(" ").some(part=>part.startsWith(word))));
}
export function snapshotPage(section:Section,query:string,page:number) {
  const rows=snapshotRows(section,query);
  const offset=query?0:(Math.max(1,page)-1)*PAGE_SIZE;
  return {items:rows.slice(offset,offset+(query?80:PAGE_SIZE)),indexCount:rows.length,hasMore:offset+PAGE_SIZE<rows.length};
}
