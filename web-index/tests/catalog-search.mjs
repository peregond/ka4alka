import assert from 'node:assert/strict';
import {catalogModules} from './helpers/catalog-modules.mjs';

const movie=(slug,title,year,extra={})=>({id:'movies:'+slug,section:'movies',title,year,pageUrl:'https://w6.zona.plus/movies/'+slug,...extra});
const newer=movie('matrix-2021','Матрица: Воскрешение',2021,{description:'Сохранённое описание.',kinopoisk:'7.1'});
const older=movie('matrix-1999','Матрица',1999);
const remade=movie('matrix-remake','Матрица',2030);
let now=Date.now();class Clock extends Date {static now(){return now}}
const load=catalogModules({}, {Date:Clock});
const search=load('@/lib/catalog-search');
const merged=search.mergeCatalogSearch('movies','матрица',[newer],[older,{...newer,description:null,kinopoisk:null}],[remade]);
assert.equal(merged.length,3);assert.equal(merged[0].id,older.id);
assert.equal(merged.find(item=>item.id===newer.id).description,newer.description);
assert.equal(merged.find(item=>item.id===newer.id).kinopoisk,'7.1');
assert(search.catalogSearchScore(movie('alias','Название проверки',2020,{aliases:['Проверочный перевод']}),'проверочный перевод')>0);
assert.equal(search.catalogSearchScore(older,'Матрица 2021'),0);
assert(search.catalogSearchScore(movie('numbers','Бегущий по лезвию 2049',2017),'Бегущий по лезвию 2049')>0);
assert.equal(search.mergeCatalogSearch('movies','матрица',[{...older,section:'series'}]).length,0);
const requestUrls=[];
const transport=catalogModules({}, {fetch:async url=>{requestUrls.push(url);return new Response('<ul></ul>')}})('@/lib/catalog-source');
await transport.fetchCatalog('movies','Не стесняйся'.normalize('NFD'),1);
assert.equal(new URL(requestUrls[0]).searchParams.get('query'),'Не стесняйся','canonical Unicode is sent to the source too, rather than only fixed during local matching');
let calls=0,release;
const gate=new Promise(resolve=>release=resolve);
const first=search.completeCatalogSearch('movies','матрица',async()=>{calls++;return gate});
const second=search.completeCatalogSearch('movies','матрица',async()=>{calls++;return []});
release([older,newer]);await Promise.all([first,second]);assert.equal(calls,1);
assert.equal((await search.completeCatalogSearch('movies','матрица',async()=>[])).length,2);
now+=16*60_000;await search.completeCatalogSearch('movies','матрица',async()=>{calls++;throw Error('offline')});
await search.completeCatalogSearch('movies','матрица',async()=>{calls++;return []});assert.equal(calls,2);

async function route({offline=false,storageOffline=false,query='матрица'}={}) {
  let upstream=0;const base=catalogModules()('@/lib/catalog-source');
  const routes=catalogModules({
    '@/lib/catalog-source':{...base,fetchCatalog:async()=>{upstream++;if(offline)throw Error('source unavailable');return [older]}},
    '@/lib/shared-catalog':{sharedCatalogPage:async()=>({items:[newer],indexCount:3000,hasMore:true})},
    '@/lib/catalog-snapshot':{snapshotPage:()=>({items:[newer],indexCount:3000,hasMore:true}),PAGE_LIMIT:250},
    '@/lib/index-store':{readMedia:async()=>[newer],lastSynced:async()=>Date.now(),countMedia:async()=>3000,
      upsertMedia:async()=>{if(storageOffline)throw Error('D1 unavailable')},markSynced:async()=>{}},
  });
  const GET=routes('@/app/api/catalog/route').GET;
  const request=new Request('https://example.test/api/catalog?section=movies&q='+encodeURIComponent(query));
  const a=await GET(request);assert.equal(a.status,200);const body=await a.json();
  if(query&&!offline){assert(body.items.some(item=>item.id===older.id));assert(body.items.some(item=>item.id===newer.id));assert.equal(body.items.length,2);}
  else assert.equal(body.items.length,1);
  await GET(request);assert.equal(upstream,query?1:0,'local matches or a fresh D1 timestamp cannot prevent the first full lookup; exact query results avoid duplicate requests');
}
await route();await route({storageOffline:true});await route({offline:true});await route({query:''});
console.log('PASS: partial local catalog cannot shadow older films; exact query cache, bounded coalescing, source/storage failures, aliases, years, remakes and browse fast path');
