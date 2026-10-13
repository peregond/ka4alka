import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const dictionaries = Object.fromEntries(['ru','en','es'].map(language => [language,JSON.parse(fs.readFileSync(new URL(`../lib/locales/${language}.json`,import.meta.url)))]));
const code=ts.transpileModule(fs.readFileSync(new URL('../lib/site-language.ts',import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
const exports={};
vm.runInNewContext(code,{exports,Intl,require:name=>dictionaries[name.match(/(ru|en|es)\.json$/)[1]]});
const {normalizeLanguage,readPreference,resolveLanguage,translate,catalogCount,downloadSize}=exports;
assert.equal(normalizeLanguage('es-MX'),'es');
assert.equal(normalizeLanguage('EN_us'),'en');
assert.equal(normalizeLanguage('ru-RU'),'ru');
assert.equal(normalizeLanguage(null),null);
assert.equal(normalizeLanguage('fr-FR'),null);
assert.equal(readPreference('en'),'en');
assert.equal(readPreference('obsolete'),'auto');
assert.equal(resolveLanguage('auto',['es-AR','en-US']),'es');
assert.equal(resolveLanguage('auto',['fr-FR','ru-RU']),'ru');
assert.equal(resolveLanguage('auto',['de-DE']),'en');
assert.equal(resolveLanguage('es',['ru-RU']),'es','manual choice overrides browser');
assert.equal(resolveLanguage('auto',[]),'en');
assert.equal(catalogCount('ru',1,'movies'),'1 фильм');
assert.equal(catalogCount('ru',22,'movies'),'22 фильма');
assert.equal(catalogCount('ru',11,'series'),'11 сериалов');
assert.equal(catalogCount('en',1,'series'),'1 TV show');
assert.equal(catalogCount('es',2,'movies'),'2 películas');
assert.equal(translate('es','Открыть {title}',{title:'South Park'}),'Abrir South Park');
assert.equal(translate('en','отдают: {count}',{count:0}),'seeders: 0');
assert.equal(downloadSize('en',1073741824),'1.0 GB');
assert.equal(downloadSize('es',1073741824),'1,0 GB');
assert.equal(downloadSize('ru',null),'Размер не указан');
for(const language of ['en','es']){
 assert.deepEqual(Object.keys(dictionaries[language]).sort(),Object.keys(dictionaries.ru).sort());
 for(const [key,value] of Object.entries(dictionaries[language])){
  assert.ok(value.trim(),`${language}: empty translation for ${key}`);
  assert.deepEqual((value.match(/\{\w+\}/g)??[]).sort(),(key.match(/\{\w+\}/g)??[]).sort(),`${language}: placeholders for ${key}`);
 }
}
// Ensure each literal translation call resolves; guards accidental whitespace keys.
for(const name of ['catalog-page.tsx','release-list.tsx','product-preview.tsx','site-language.tsx']){
 const source=ts.createSourceFile(name,fs.readFileSync(new URL(`../components/${name}`,import.meta.url),'utf8'),ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
 const visit=node=>{
  if(ts.isCallExpression(node)&&ts.isIdentifier(node.expression)&&node.expression.text==='t'&&ts.isStringLiteral(node.arguments[0]))assert.ok(node.arguments[0].text in dictionaries.ru,`${name}: missing ${node.arguments[0].text}`);
  if(ts.isJsxText(node))assert.ok(node.text.trim() === 'Русский' || !/[А-Яа-яЁё]/.test(node.text),`${name}: untranslated JSX: ${node.text}`);
  ts.forEachChild(node,visit);
 }; visit(source);
}
console.log('Site language checks passed: locale detection, manual override, plurals, formatting, dictionaries and UI coverage.');
