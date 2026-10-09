import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
let now=Date.UTC(2026,9,7,6);
class Clock extends Date {static now(){return now}}
const seed=JSON.parse(fs.readFileSync(new URL('../app/data/seed.json',import.meta.url),'utf8'));
const feed={schemaVersion:1,generatedAtUtc:new Date(now).toISOString(),items:seed};
let calls=0,offline=false;
function load(file,dependencies={},globals={}) {
  const code=ts.transpileModule(fs.readFileSync(new URL(file,import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
  const exports={};vm.runInNewContext(code,{exports,require:name=>dependencies[name],Response,Request,URL,Date:Clock,Number,Math,AbortSignal,TextDecoder,...globals});return exports;
}
const catalogSource=load('../lib/catalog-source.ts');
const source=load('../lib/shared-catalog.ts',{'./catalog-source':catalogSource}, {fetch:async()=>{calls++;if(offline)throw new Error('offline');await Promise.resolve();return Response.json(feed)}});
assert.equal(source.refreshBoundary(Date.UTC(2026,9,7,4,59)),Date.UTC(2026,9,6,5));
assert.equal(source.refreshBoundary(Date.UTC(2026,9,7,5)),Date.UTC(2026,9,7,5));
assert.throws(()=>source.parseSharedCatalog({...feed,items:seed.slice(0,1)}),/Incomplete/);
assert.throws(()=>source.parseSharedCatalog({...feed,schemaVersion:2}),/Invalid/);
assert.throws(()=>source.parseSharedCatalog({...feed,generatedAtUtc:new Date(now+86400000).toISOString()}),/Invalid/);
const [first,series]=await Promise.all([source.sharedCatalogPage('movies','',1),source.sharedCatalogPage('series','',1)]);
assert.equal(calls,1);assert.equal(first.items.length,40);assert.equal(series.items.length,40);assert(first.hasMore);
const found=await source.sharedCatalogPage('movies',first.items[0].title,1);assert(found.items.some(item=>item.id===first.items[0].id)&&!found.hasMore);
const distant=await source.sharedCatalogPage('movies','',50);
assert.equal(calls,1);assert.equal(distant.items.length,40);assert(!distant.items.some(item=>first.items.some(prior=>prior.id===item.id)));
assert(first.items.every((item,index)=>index===0||Math.min(item.year,2026)<=Math.min(first.items[index-1].year,2026)));
now+=86400000;offline=true;
const saved=await source.sharedCatalogPage('movies','',1);assert.equal(saved.items[0].id,first.items[0].id);assert.equal(calls,2);
await source.sharedCatalogPage('series','',2);assert.equal(calls,2);
const route=load('../app/api/catalog/route.ts',{
  '@/lib/shared-catalog':source,'@/lib/catalog-source':{normalize:value=>value},'@/lib/catalog-snapshot':{PAGE_LIMIT:250,snapshotPage:()=>({items:[],indexCount:0,hasMore:false})},
  '@/lib/index-store':{upsertMedia:async()=>{throw new Error('D1 unavailable')}}
});
const response=await route.GET(new Request('https://example.test/api/catalog?section=movies&page=50'));
assert.equal(response.status,200);assert.equal((await response.json()).items.length,40);
const view=load('../lib/catalog-view.ts');const old=seed.find(item=>item.section==='movies'),other=seed.find(item=>item.section==='series'),fresh={...old,id:'movies:new-fixture',title:'Новый фильм'};
const merged=view.mergeCatalogPage([old,other],[fresh],'movies',1,'');assert.equal(merged[0].id,fresh.id);assert(!merged.some(item=>item.id===old.id));assert.equal(merged[1].id,other.id);
const next=view.mergeCatalogPage(merged,[fresh,old],'movies',2,'');assert.equal(next.filter(item=>item.id===fresh.id).length,1);assert.equal(next[0].id,fresh.id);
console.log('PASS: daily boundary, complete feed validation, shared concurrent requests, all pages, offline retention, D1-independent route and fresh-first browser ordering');
