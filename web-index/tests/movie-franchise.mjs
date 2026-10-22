import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
function load(file, dependencies = {}) {
 const exports = {};
 const code = ts.transpileModule(fs.readFileSync(new URL(file, import.meta.url), 'utf8'), {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 vm.runInNewContext(code,{exports,require:name=>dependencies[name],Response,Request,URL,AbortSignal,Uint8Array,TextDecoder});return exports;
}
const info=load('../lib/title-info.ts');
const backdrop=load('../lib/tmdb-backdrop.ts');
const api=load('../lib/movie-franchise.ts',{'@/lib/title-info':info,'@/lib/tmdb-backdrop':backdrop});
const identity={section:'movies',title:'Same name',year:2026,imdbId:'tt1234561',language:'ru'};
let calls=[],active=0,peak=0,mode='normal';
async function fake(url, options) {
 assert.equal(options.headers.Authorization,'Bearer fixture-token');assert.equal(options.redirect,'manual');assert(!url.includes('fixture-token'));
 calls.push(url);active++;peak=Math.max(peak,active);await Promise.resolve();active--;
 if(mode==='redirect')return new Response(null,{status:302,headers:{Location:'https://untrusted.test'}});
 if(url.includes('/find/'))return Response.json({movie_results:[{id:1}]});
 if(url.includes('/collection/'))return Response.json({id:10,name:'A collection',parts:mode==='wrong-member'?[{id:2}]:[{id:1},{id:2},{id:3}]});
 const id=Number(url.match(/movie\/(\d+)/)[1]);
 return Response.json({id,title:'Part '+id,imdb_id:'tt123456'+id,original_title:'Part '+id,release_date:id===1?'2020-01-01':'2026-01-01',belongs_to_collection:mode==='standalone'?null:{id:10},overview:'Overview',poster_path:'/poster.jpg',vote_average:8.2});
}
const result=await api.fetchFranchise(identity,'fixture-token',fake);
assert.equal(result.id,10);assert.deepEqual(Array.from(result.items,x=>x.imdbId),['tt1234561','tt1234562','tt1234563']);assert(peak<=3);assert(result.items.every(x=>x.poster==='https://image.tmdb.org/t/p/w500/poster.jpg'));
mode='standalone';assert.equal(await api.fetchFranchise(identity,'fixture-token',fake),null);
mode='wrong-member';await assert.rejects(api.fetchFranchise(identity,'fixture-token',fake),/Collection identity/);
mode='redirect';calls=[];await assert.rejects(api.fetchFranchise(identity,'fixture-token',fake));assert.equal(calls.length,1);
mode='normal';let count=0,finish;
const cached=api.franchiseCache(async()=>{count++;await new Promise(r=>{finish=r});return result;});
const a=cached('tt1234567',identity),b=cached('tt1234567',identity);await Promise.resolve();finish();await a;await b;await cached('tt1234567',identity);assert.equal(count,1);
const en=cached('tt1234567',{...identity,language:'en'});await Promise.resolve();finish();await en;assert.equal(count,2,'RU and EN caches cannot mix');
const env={TMDB_READ_TOKEN:'fixture-token'};let seen;
const route=load('../app/api/franchise/route.ts',{'cloudflare:workers':{env},'@/app/data/seed.json':[],'@/lib/index-store':{getMedia:async()=>null},'@/lib/shared-catalog':{sharedCatalogItem:async()=>null},'@/lib/movie-franchise':{franchiseCache:()=>async(key,item)=>{seen=item;return result},fetchFranchise:api.fetchFranchise}});
assert.equal((await route.GET(new Request('https://test/api/franchise?imdb=tt1234567&language=en'))).status,200);assert.equal(seen.imdbId,'tt1234567');assert.equal(seen.language,'en');
for(const query of ['imdb=https://evil.test','id=series:test','id=movies:../../bad'])assert.equal((await route.GET(new Request('https://test/api/franchise?'+query))).status,400);
env.TMDB_READ_TOKEN='';assert.equal((await route.GET(new Request('https://test/api/franchise?imdb=tt1234567'))).status,503);
console.log('PASS: exact collection membership, no fuzzy grouping, chronological parts, bounded workers, redirect refusal, coalescing, language separation and route validation');
