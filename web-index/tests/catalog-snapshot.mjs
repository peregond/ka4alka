import assert from 'node:assert/strict';
import fs from 'node:fs';
import {catalogModules} from './helpers/catalog-modules.mjs';
const seed=JSON.parse(fs.readFileSync(new URL('../app/data/seed.json',import.meta.url),'utf8'));
const live=catalogModules({}, {fetch:async url=>{
  const page=Number(new URL(url).searchParams.get('page'));
  const html=Array.from({length:60},(_,i)=>{const id=(page-1)*60+i+1;return `<li class="results-item-wrap"><a itemprop="url" href="/movies/fixture-${id}"><span itemprop="name">Фильм ${id}</span></a></li>`}).join('');
  return new Response(html);
}})('@/lib/catalog-source');
const livePages=[];for(let page=1;page<=3;page++)livePages.push(...await live.fetchCatalog('movies','',page));
assert.deepEqual(livePages.map(x=>x.id),Array.from({length:120},(_,i)=>'movies:fixture-'+(i+1)));
const modules=catalogModules();
const source=modules('@/lib/catalog-source');
const snapshot=modules('@/lib/catalog-snapshot');
assert(snapshot.snapshotRows('movies').length>=2000);
let rows=[];
for(let page=1;page<=Math.ceil(snapshot.snapshotRows('movies').length/40);page++)rows.push(...snapshot.snapshotPage('movies','',page).items);
assert.equal(new Set(rows.map(x=>x.id)).size,rows.length);
assert.equal(rows.length,snapshot.snapshotRows('movies').length);
const failures={readMedia:async()=>{throw new Error('D1 unavailable')}};
let indexed=[];
const available={readMedia:async()=>[],lastSynced:async()=>0,upsertMedia:async rows=>{indexed=rows},countMedia:async()=>0,markSynced:async()=>{}};
for(const store of [failures,available]) {
  const route=catalogModules({'@/lib/catalog-source':{...source,fetchCatalog:async()=>{throw new Error('Source unavailable')}},'@/lib/index-store':store,'@/lib/shared-catalog':{sharedCatalogPage:async()=>null}})('@/app/api/catalog/route');
  const get=async page=>{const response=await route.GET(new Request('https://example.com/api/catalog?section=movies&page='+page));assert.equal(response.status,200);return response.json()};
  const first=await get(1),fiftieth=await get(50);
  assert.equal(first.items.length,40);assert.equal(fiftieth.items.length,40);assert(first.hasMore);assert(fiftieth.indexCount>=2000);
  assert(!fiftieth.items.some(x=>first.items.some(y=>x.id===y.id)));
  const last=Math.ceil(fiftieth.indexCount/40);assert.equal((await get(last)).hasMore,false);assert.equal((await get(last+1)).items.length,0);
  assert.equal((await get('Infinity')).page,1);
}
assert.equal(indexed.length,40);
console.log('PASS: over 2000 unique movies, all snapshot pages, offline D1/source page 50, exact last page, safe page parsing and per-page indexing');
