import assert from 'node:assert/strict';
import { catalogModules } from './helpers/catalog-modules.mjs';

let now=Date.UTC(2026,9,11,6);
class Clock extends Date {static now(){return now}}
const load=fetch=>catalogModules({}, {Date:Clock,fetch})('@/lib/shared-catalog');
const rows=Array.from({length:2300},(_,index)=>{
  const section=index<2000?'movies':'series',slug='fixture-'+index;
  return {id:section+':'+slug,section,title:'Фильм '+index,year:2026,
    pageUrl:'https://w6.zona.plus/'+(section==='movies'?'movies':'tvseries')+'/'+slug,
    description:'Текст '.repeat(500),people:[{name:'Актёр',role:'Актёры',pageUrl:''}]};
});
const feed={schemaVersion:1,generatedAtUtc:new Date(now).toISOString(),items:rows};
const payload=new TextEncoder().encode(JSON.stringify(feed));
assert(payload.byteLength>4*1024*1024);
function streamed(bytes){
  let offset=0;
  return new Response(new ReadableStream({pull(controller){
    if(offset===bytes.length){controller.close();return}
    const end=Math.min(bytes.length,offset+65535);
    controller.enqueue(bytes.subarray(offset,end));offset=end;
  }}));
}
let badRefresh=false,cancelled=false;
const source=load(async()=>badRefresh?oversizedResponse():streamed(payload));
const decoded=await source.readSharedCatalog(streamed(payload));
assert.equal(decoded.items.length,2300);
assert.equal(decoded.items[0].description,rows[0].description);
const page=await source.sharedCatalogPage('movies','',1);
assert.equal(page.items[0].people[0].name,'Актёр');
function oversizedResponse(){
  return new Response(new ReadableStream({pull(controller){
    controller.enqueue(new Uint8Array(source.MAX_SHARED_CATALOG_BYTES));
    controller.enqueue(new Uint8Array([1]));
  },cancel(){cancelled=true}}));
}
await assert.rejects(source.readSharedCatalog(oversizedResponse()),/Oversized/);
assert(cancelled);
let bodyRead=false;
const declared=new Response(new ReadableStream({pull(){bodyRead=true}}),
  {headers:{'Content-Length':String(source.MAX_SHARED_CATALOG_BYTES+1)}});
await assert.rejects(source.readSharedCatalog(declared),/unavailable/);
assert(!bodyRead);
now+=86400000;badRefresh=true;
const retained=await source.sharedCatalogPage('movies','',1);
assert.equal(retained.items[0].description,page.items[0].description);
assert.equal(retained.items[0].people[0].name,'Актёр');
console.log('PASS: text feed above 4 MiB, split UTF-8 streaming, actual/declared 32 MiB bounds, overflow cancellation and last-good retention');
