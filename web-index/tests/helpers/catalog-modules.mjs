import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';
import ts from 'typescript';

const root=fileURLToPath(new URL('../../',import.meta.url));
export function catalogModules(overrides={},globals={}) {
  const cache=new Map();
  function load(name,parent=root) {
    if(Object.hasOwn(overrides,name))return overrides[name];
    let file=name.startsWith('@/')?path.join(root,name.slice(2)):path.resolve(parent,name);
    if(!path.extname(file))file+='.ts';
    if(cache.has(file))return cache.get(file);
    if(file.endsWith('.json'))return JSON.parse(fs.readFileSync(file,'utf8'));
    const exports={};cache.set(file,exports);
    const code=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
    vm.runInNewContext(code,{exports,require:name=>load(name,path.dirname(file)),Date,Map,Set,JSON,Number,Math,Promise,Response,URL,URLSearchParams,AbortSignal,TextDecoder,TextEncoder,fetch,console,...globals},{filename:file});
    return exports;
  }
  return name=>load(name);
}
