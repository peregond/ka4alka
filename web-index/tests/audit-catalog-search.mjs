import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {catalogModules} from './helpers/catalog-modules.mjs';

const option=name=>{const i=process.argv.indexOf(name);return i>=0?process.argv[i+1]:undefined};
const input=option('--catalog')??new URL('../app/data/seed.json',import.meta.url);
const output=option('--report')??'test-output/server-search-audit.json';
const raw=fs.readFileSync(input),data=JSON.parse(raw),rows=Array.isArray(data)?data:data.items;
if(!Array.isArray(rows)||!rows.length)throw Error('Cannot audit an empty catalog');
const load=catalogModules(),{catalogSearchScore}=load('@/lib/catalog-search'),{enrichMedia,mediaNames}=load('@/lib/catalog-identity');
const failures=[];let queries=0,names=0;
for(const value of rows) {
  const item=enrichMedia(value),aliases=mediaNames(item);names+=aliases.length;
  for(const name of aliases)for(const query of new Set([name,name.normalize('NFD'),...(item.year?[name+' '+item.year]:[])])) {
    queries++;
    if(!catalogSearchScore(item,query))failures.push({id:item.id,title:item.title,query,reason:'own_name_not_matched'});
  }
  if(!aliases.length)failures.push({id:item.id,title:item.title,reason:'no_indexed_names'});
}
const report={schemaVersion:1,generatedAtUtc:new Date().toISOString(),scope:'server-local-matching',sha256:crypto.createHash('sha256').update(raw).digest('hex'),networkRequests:0,
  records:rows.length,movies:rows.filter(row=>row.section==='movies').length,series:rows.filter(row=>row.section==='series').length,names,queries,failures};
fs.mkdirSync(path.dirname(path.resolve(output)),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));
console.log(`Server audit: ${rows.length} records, ${queries} queries, ${failures.length} failures. Report: ${output}`);
if(failures.length)process.exitCode=1;
