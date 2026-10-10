import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';

function load(file,dependencies={},globals={}) {
  const code=ts.transpileModule(fs.readFileSync(new URL(file,import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
  const exports={};vm.runInNewContext(code,{exports,require:name=>dependencies[name],Response,Request,URL,Number,Math,AbortSignal,TextDecoder,Uint8Array,...globals});return exports;
}
let upstream,requested=[];
const relay=load('../lib/torrent-relay.ts',{},{fetch:async(url,init)=>{requested.push({url,init});return upstream();}});

// Only the exact download addresses produced by the release sources are relayed.
for(const url of ['https://nnmclub.to/forum/download.php?id=1702634','https://megapeer.vip/download/180694','https://bigfangroup.org/download.php?id=285126','https://nyaa.si/download/1951823.torrent'])
  assert.equal(relay.relaySource(url)?.href,url,url);
for(const url of [null,'','http://nnmclub.to/forum/download.php?id=1','https://nnmclub.to/forum/viewtopic.php?t=1','https://nnmclub.to/forum/download.php?id=1&x=2','https://evil.example/download/1',
  'https://megapeer.vip/download/1#x','https://user@megapeer.vip/download/1','https://megapeer.vip:8443/download/1','https://sub.nnmclub.to/forum/download.php?id=1','https://archive.org/download/a/a.torrent','magnet:?xt=urn:btih:'+'a'.repeat(40)])
  assert.equal(relay.relaySource(url),null,String(url));
assert.equal(relay.relayPath('https://megapeer.vip/download/5'),'/api/torrent?url=https%3A%2F%2Fmegapeer.vip%2Fdownload%2F5');
assert.equal(relay.relayPath('magnet:?xt=urn:btih:'+'a'.repeat(40)),null);
const source=new URL('https://megapeer.vip/download/5');
assert(relay.sameTracker(source,'https://megapeer.vip/download/5')&&relay.sameTracker(source,'https://dl.megapeer.vip/x.torrent'));
assert(!relay.sameTracker(source,'https://megapeer.vip.evil.example/x')&&!relay.sameTracker(source,'http://megapeer.vip/download/5')&&!relay.sameTracker(source,'not a url'));

// Bencode check: a real torrent shape passes; block pages, truncated and malformed files fail.
const bytes=value=>new TextEncoder().encode(value);
const join=(...parts)=>{const out=new Uint8Array(parts.reduce((n,p)=>n+p.length,0));let at=0;for(const p of parts){out.set(p,at);at+=p.length;}return out;};
const pieces=new Uint8Array(20).fill(0xff);
const str=value=>{const raw=typeof value==='string'?bytes(value):value;return join(bytes(raw.length+':'),raw);};
const torrent=join(bytes('d'),str('announce'),str('udp://tracker.opentrackr.org:1337/announce'),str('creation date'),bytes('i1700000000e'),str('info'),
  bytes('d'),str('length'),bytes('i262144e'),str('name'),str('Фильм.mkv'),str('piece length'),bytes('i16384e'),str('pieces'),str(pieces),bytes('ee'));
assert(relay.isTorrent(torrent));
assert(!relay.isTorrent(bytes('<html><body>Доступ к ресурсу ограничен</body></html>')));
assert(!relay.isTorrent(torrent.subarray(0,torrent.length-1)));
assert(!relay.isTorrent(join(torrent,bytes('x'))));
assert(!relay.isTorrent(bytes('d8:announce3:urle')),'a dictionary without info is not a torrent');
assert(!relay.isTorrent(bytes('d4:infoi1ee')),'info must be a dictionary');
assert(!relay.isTorrent(bytes('l4:infod1:ai1eee')),'top level must be a dictionary');
assert(!relay.isTorrent(bytes('d4:infod1:ai1ee5:extra99:shortee')));
assert(!relay.isTorrent(bytes('d'+'l'.repeat(80)+'e'.repeat(80)+'4:infod1:ai1eee')),'deep nesting is rejected');

// Route: validates the address, relays a real torrent unchanged and rejects anything else.
const route=load('../app/api/torrent/route.ts',{'@/lib/torrent-relay':relay});
assert.equal(route.GET,relay.relayTorrent,'the site route and the standalone relay share one handler');
const get=url=>route.GET(new Request('https://site.test/api/torrent'+(url===undefined?'':'?url='+encodeURIComponent(url))));
const withUrl=(response,url)=>{Object.defineProperty(response,'url',{value:url});return response;};
let response=await get('https://evil.example/download/1');assert.equal(response.status,400);assert.equal(requested.length,0);
response=await get(undefined);assert.equal(response.status,400);
upstream=()=>withUrl(new Response(torrent,{headers:{'Content-Type':'application/x-bittorrent'}}),'https://megapeer.vip/download/5');
response=await get('https://megapeer.vip/download/5');
assert.equal(response.status,200);assert.equal(response.headers.get('content-type'),'application/x-bittorrent');
assert.match(response.headers.get('content-disposition'),/megapeer-5\.torrent/);
assert.deepEqual(new Uint8Array(await response.arrayBuffer()),torrent);
assert.equal(requested.at(-1).url,'https://megapeer.vip/download/5');
upstream=()=>withUrl(new Response('<html>blocked</html>',{headers:{'Content-Type':'text/html'}}),'https://megapeer.vip/download/5');
response=await get('https://megapeer.vip/download/5');assert.equal(response.status,502);assert.match((await response.json()).error,/не torrent/);
upstream=()=>withUrl(new Response('nope',{status:404}),'https://megapeer.vip/download/5');
response=await get('https://megapeer.vip/download/5');assert.equal(response.status,502);
upstream=()=>withUrl(new Response(torrent),'https://other.example/file.torrent');
response=await get('https://megapeer.vip/download/5');assert.equal(response.status,502);assert.match((await response.json()).error,/другой сайт/);
upstream=()=>withUrl(new Response(torrent,{headers:{'Content-Length':String(11*1024*1024)}}),'https://megapeer.vip/download/5');
response=await get('https://megapeer.vip/download/5');assert.equal(response.status,413);
upstream=()=>{throw new TypeError('network');};
response=await get('https://nnmclub.to/forum/download.php?id=7');assert.equal(response.status,502);

// BigFanGroup: the real public rows used by the Windows tests parse identically on the site.
const catalogSource=load('../lib/catalog-source.ts');
const sources=load('../lib/release-sources.ts',{'./catalog-source':catalogSource});
const fixture=name=>fs.readFileSync(new URL(`../../tests/Fixtures/${name}.html`,import.meta.url),'utf8');
const film={id:'movies:interstellar',section:'movies',title:'Интерстеллар',originalTitle:'Interstellar',year:2014,pageUrl:'https://w6.zona.plus/movies/interstellar'};
const series={id:'series:south-park',section:'series',title:'Южный парк',originalTitle:'South Park',year:1997,pageUrl:'https://w6.zona.plus/tvseries/south-park'};
const films=sources.parseBigFanGroup(fixture('bigfangroup-film'),film);
assert.equal(films.length,1);
assert.equal(films[0].id,'BigFanGroup:285126');assert.equal(films[0].seeds,421);assert.equal(films[0].size,4273492459);
assert.equal(films[0].torrentUrl,'https://bigfangroup.org/download.php?id=285126');assert.equal(films[0].pageUrl,'https://bigfangroup.org/details.php?id=285126');
assert(relay.relaySource(films[0].torrentUrl));
const shows=sources.parseBigFanGroup(fixture('bigfangroup-series'),series);
assert.equal(shows.length,1);assert.equal(shows[0].id,'BigFanGroup:344266');assert.match(shows[0].title,/Сезоны 1-20/);assert.equal(shows[0].quality,'1080p');
assert.equal(sources.parseBigFanGroup(fixture('bigfangroup-film'),{...film,year:2015}).length,0,'wrong film year');
assert.equal(sources.parseBigFanGroup(fixture('bigfangroup-series'),{...series,section:'movies'}).length,0,'series in a film search');
assert.equal(sources.parseBigFanGroup(fixture('bigfangroup-film'),series).length,0,'film in a series search');
assert.equal(sources.parseBigFanGroup('<html>Доступ ограничен</html>',film).length,0);

// Stateless search for the standalone relay: validated card, same labels as /api/releases.
const params=value=>new URLSearchParams(value);
const searchCatalog=load('../lib/catalog-source.ts');
let pages={};
const searchSources=load('../lib/release-sources.ts',{'./catalog-source':searchCatalog},{fetch:async url=>{const page=Object.entries(pages).find(([prefix])=>String(url).startsWith(prefix));if(!page)throw new TypeError('offline');return new Response(page[1]);}});
const search=load('../lib/release-search.ts',{'./catalog-source':searchCatalog,'./release-sources':searchSources});
const card=search.searchMedia(params('id=movies:interstellar&title=Интерстеллар&original=Interstellar&year=2014'));
assert.deepEqual({...card},{id:'movies:interstellar',section:'movies',title:'Интерстеллар',originalTitle:'Interstellar',year:2014,pageUrl:'https://w6.zona.plus/movies/interstellar'});
assert.equal(search.searchMedia(params('id=series:south-park&title=Южный парк&year=1997')).pageUrl,'https://w6.zona.plus/tvseries/south-park');
for(const bad of ['title=x','id=movies:a&title=','id=games:a&title=x','id=movies:a/b&title=x','id=movies:a&title=x&year=1.5','id=movies:a&title=x&year=3000','id=movies:a&title='+'x'.repeat(201)])
  assert.equal(search.searchMedia(params(bad)),null,bad);
assert.deepEqual({...search.presentRelease({source:'RuTracker.org · Knaben',id:'a'})},{source:'RuTracker',id:'a',via:'Knaben'});
assert.deepEqual({...search.presentRelease({source:'NNM-Club',id:'b'})},{source:'NNM-Club',id:'b',via:null});
let reply=await search.searchReleases(new Request('https://relay.test/api/search?id=movies:x'));
assert.equal(reply.status,400);
// CP1251 bytes, as BigFanGroup serves them.
const cp1251=text=>Uint8Array.from([...text].map(char=>{const code=char.codePointAt(0);return code<128?code:code>=0x410&&code<=0x44F?code-0x350:char==='ё'?0xB8:char==='Ё'?0xA8:0x3F;}));
pages={'https://bigfangroup.org/browse.php':cp1251(fixture('bigfangroup-film'))};
reply=await search.searchReleases(new Request('https://relay.test/api/search?id=movies:interstellar&title='+encodeURIComponent('Интерстеллар')+'&original=Interstellar&year=2014'));
assert.equal(reply.status,200);
const found=await reply.json();
assert.equal(found.items.length,1,'other sources are offline in this test');
assert.equal(found.items[0].mediaId,'movies:interstellar');assert.equal(found.items[0].source,'BigFanGroup');
assert(relay.relaySource(found.items[0].torrentUrl),'the found BigFanGroup torrent can be fetched through the relay');
pages={};
reply=await search.searchReleases(new Request('https://relay.test/api/search?id=movies:interstellar&title=x&original=y&year=2014'));
assert.equal(reply.status,200);assert.deepEqual((await reply.json()).items,[]);
console.log('torrent relay, stateless search and BigFanGroup checks passed');
