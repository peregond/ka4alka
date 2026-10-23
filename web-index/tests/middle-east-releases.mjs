import assert from 'node:assert/strict';
import fs from 'node:fs';
import {catalogModules} from './helpers/catalog-modules.mjs';

const fixture=JSON.parse(fs.readFileSync(new URL('./fixtures/knaben-middle-east-2026.json',import.meta.url),'utf8'));
const english='Once Upon a Time in the Middle East';
const queries=[];
const modules=catalogModules({}, {fetch:async value=>{
  const url=new URL(value);
  if(url.hostname==='api.knaben.org'){
    queries.push(url.searchParams.get('q'));
    return Response.json({hits:url.searchParams.get('q')===english?fixture.hits:[]});
  }
  if(url.hostname==='apibay.org')return Response.json([]);
  return new Response('{}');
}});
const {enrichMedia}=modules('@/lib/catalog-identity');
const {collectReleases,matches}=modules('@/lib/release-sources');
const {presentRelease}=modules('@/lib/release-search');
const movie={id:'movies:odnazhdy-na-blizhnem-vostoke',section:'movies',title:'Однажды на Ближнем Востоке',year:2026,originalTitle:'Huan ying laai long can guan',pageUrl:'https://w6.zona.plus/movies/odnazhdy-na-blizhnem-vostoke'};
const enriched=enrichMedia(movie);
assert.equal(enriched.imdbId,'tt34386754');
const result=await collectReleases(movie);
assert.equal(result.length,1,'Russian and transliterated searches cannot hide a release indexed under the verified English title');
assert(queries.includes(english));assert(queries.length<=4);
const release=presentRelease(result[0]);
assert.equal(release.source,'The Pirate Bay');assert.equal(release.via,'Knaben');
assert.equal(release.seeds,411);assert.equal(release.quality,'1080p');
assert(release.torrentUrl.includes('F81D21727E4DB6E7E70290230C230570108473BB'));
assert.equal(matches(enriched,fixture.hits[0].title,'Knaben'),true);
assert.equal(matches({...enriched,year:2025},fixture.hits[0].title,'Knaben'),false);
assert.equal(enrichMedia({...movie,year:2025}).imdbId,undefined);
assert.equal(enrichMedia({...movie,section:'series'}).imdbId,undefined);
assert.equal(enrichMedia({...movie,imdbId:'tt0000001'}).aliases,undefined);
console.log('PASS: reported film resolves by exact verified identity, queries its English alias and returns the observed TPB/Knaben WEB release; different years, sections and identities remain separate');
