import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import vm from 'node:vm';
import ts from 'typescript';
function load(file,dependencies={}) {
  const code=ts.transpileModule(fs.readFileSync(new URL(file,import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
  const exports={};vm.runInNewContext(code,{exports,require:name=>dependencies[name],Date,Map,Set,JSON,Number,Promise,Response,URL});return exports;
}
const aliases=JSON.parse(fs.readFileSync(new URL('../app/data/title-aliases.json',import.meta.url),'utf8'));
const identity=load('../lib/catalog-identity.ts',{'@/app/data/title-aliases.json':aliases});
const solar={id:'series:solar',section:'series',title:'Солнечные противоположности',year:2020,pageUrl:'https://w6.zona.plus/tvseries/solar'};
const enriched=identity.enrichMedia(solar);
assert.equal(enriched.imdbId,'tt8910922');assert.equal(enriched.originalTitle,'Solar Opposites');
assert(identity.mediaNames(enriched).includes('Обратная сторона Земли'));
assert.equal(identity.catalogQueries('series','Solar Opposites')[0],'Обратная сторона Земли');
assert.equal(identity.enrichMedia({...solar,year:2021}).imdbId,undefined);
assert.equal(identity.enrichMedia({...solar,section:'movies'}).imdbId,undefined);
assert.equal(identity.enrichMedia({...solar,imdbId:'tt0000001'}).originalTitle,undefined);
const title=load('../lib/title-info.ts');
const tvdb=load('../lib/tvdb-title.ts',{'@/lib/title-info':title});
const parsed=tvdb.parseSeries('tt8910922',{id:375892,name:'Solar Opposites',year:'2020',translations:{nameTranslations:[{language:'rus',name:'Обратная сторона Земли'},{language:'rus',name:'Солнечные противоположности',isAlias:true},{language:'eng',name:'Unrelated English alias',isAlias:true}]}});
assert(parsed.aliases.includes('Солнечные противоположности'));assert(!parsed.aliases.includes('Unrelated English alias'));

const directory=fs.mkdtempSync(path.join(os.tmpdir(),'ka4alka-title-store-'));
const before=process.env.DATABASE_PATH;
let database;
try {
  process.env.DATABASE_PATH=path.join(directory,'catalog.sqlite');
  const {railwayDb}=await import('../lib/railway-db.mjs');const db=database=railwayDb();
  const store=load('../lib/title-store.ts',{'@/db':{getIndexDb:()=>db}});
  assert.equal(await store.readTitle('tt8910922','series'),null);
  await store.writeTitle(parsed);
  const restored=await store.readTitle('tt8910922','series');
  assert.equal(restored.imdbId,'tt8910922');assert(restored.aliases.includes('Солнечные противоположности'));
  assert.equal(await store.readTitle('tt8910922','movies'),null);
  let upstream=0;
  const film={...parsed,imdbId:'tt1160419',section:'movies',source:'TMDB',sourceId:438631,title:'Дюна',originalTitle:'Dune',year:2021};
  const dependencies={'cloudflare:workers':{env:{TMDB_READ_TOKEN:'fixture'}},'@/lib/title-store':store,'@/lib/title-info':title,'@/lib/tmdb-title':{fetchMovie:async()=>{upstream++;return film}},'@/lib/tvdb-title':{}};
  const req={url:'https://example.test/api/title?imdb=tt1160419&section=movies'};
  assert.equal((await load('../app/api/title/route.ts',dependencies).GET(req)).status,200);
  assert.equal((await load('../app/api/title/route.ts',dependencies).GET(req)).status,200);
  assert.equal(upstream,1,'a new Worker instance uses the persistent title cache instead of another upstream lookup');
  assert.equal(await db.prepare('SELECT COUNT(*) AS n FROM railway_migrations WHERE name=?').bind('0001_title_metadata.sql').first('n'),1);
  await db.prepare('UPDATE title_metadata SET updated_at=?').bind(Date.now()-8*86400000).run();
  assert.equal(await store.readTitle('tt8910922','series'),null);
} finally {
  database?.close();
  if(before===undefined)delete process.env.DATABASE_PATH;else process.env.DATABASE_PATH=before;
  fs.rmSync(directory,{recursive:true,force:true});
}
console.log('PASS: verified multilingual aliases, ambiguous works stay separate, SQLite migration and persistent identity cache');
