import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {stripTypeScriptTypes} from 'node:module';
const read=name=>stripTypeScriptTypes(fs.readFileSync(new URL('../lib/'+name,import.meta.url),'utf8')).replace(/^import[^\n]*\n/gm,'').replace(/^export /gm,'');
const cache=vm.runInNewContext(read('release-cache.ts')+'\ncreateReleaseLookup',{Date,Map,Promise});
let clock=0,calls=0,finish;
const lookup=cache(()=>clock,30,2);
const item={id:'a'};
const load=()=>{calls++;return new Promise(resolve=>finish=resolve)};
const callers=Array.from({length:32},()=>lookup('a',load));
await Promise.resolve();assert.equal(calls,1);finish([item]);await Promise.all(callers);
assert.deepEqual(await lookup('a',()=>{throw Error('cache miss')}),[item]);
clock=31;assert.deepEqual(await lookup('a',async()=>{calls++;return []}),[]);assert.equal(calls,2);
await lookup('a',async()=>{calls++;return []});assert.equal(calls,3);
await assert.rejects(lookup('failed',async()=>{throw Error('offline')}));
assert.deepEqual(await lookup('failed',async()=>[item]),[item]);
await lookup('b',async()=>[item]);await lookup('c',async()=>[item]);
await lookup('failed',async()=>{calls++;return [item]});assert.equal(calls,4);
console.log('PASS: 32 concurrent searches share one lookup, cached replies expire, failures and empty results retry, cache remains bounded');

let pending=[],requested=[];
const context={createReleaseLookup:cache,Promise,Map,Set,URL,AbortSignal,TextDecoder,Date,
  decodeHtml:x=>x,normalize:x=>x.toLowerCase().replace(/[^\p{L}\p{N}]+/gu,' ').trim(),
  fetch:async url=>{
    requested.push(String(url));
    const body=String(url).includes('rutor.info')?'<tr><td><a href="/torrent/1">Фильм (2026) 1080p</a><a href="magnet:?xt=urn:btih:'+('a'.repeat(40))+'">magnet</a></td></tr>':'{}';
    return new Promise(resolve=>pending.push(()=>resolve(new Response(body))));
  }};
const sources=vm.runInNewContext(read('release-sources.ts')+'\n({rutor,collectReleases})',context);
const media={id:'movies:fixture',title:'Фильм',originalTitle:'Film',year:2026,section:'movies'};
const pair=sources.rutor(media);assert.equal(requested.length,2,'both RuTor title requests start before either answers');
for(const resolve of pending.splice(0))resolve();await pair;
requested=[];
let resolveAlias;const aliases=new Promise(resolve=>resolveAlias=resolve);
let aliasFactories=0;
const known={...media,originalTitle:null};
const searches=Array.from({length:32},()=>sources.collectReleases(known,()=>{aliasFactories++;return aliases}));
const search=Promise.all(searches);
await Promise.resolve();await Promise.resolve();
assert(requested.some(x=>x.includes('rutor.info'))&&requested.some(x=>x.includes('nnmclub.to')));
assert(!requested.some(x=>x.includes('knaben')),'English sources await the original title');
resolveAlias(media);
for(let i=0;i<20;i++){await Promise.resolve();for(const resolve of pending.splice(0))resolve();}
await search;
assert.equal(aliasFactories,1,'concurrent searches share metadata lookup too');
const cached=await sources.collectReleases(known,()=>{throw Error('cached results must not request metadata')});
assert(cached.length>0);
assert(requested.some(x=>x.includes('rutor.info')&&x.includes('Film')),'original-title Russian queries remain available');
assert(requested.some(x=>x.includes('knaben')),'English search resumes when aliases resolve');
console.log('PASS: metadata starts once on cache misses; RuTor queries start in parallel; Russian searches start before metadata; original-title searches are preserved');
