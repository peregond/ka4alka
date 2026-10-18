import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
import {createHash} from 'node:crypto';
const read=path=>fs.readFileSync(new URL(path,import.meta.url));
const exports={};vm.runInNewContext(ts.transpileModule(read('../lib/app-screenshots.ts').toString(),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText,{exports});
const {appScreenshot,screenshotFiles,screenshotVersion}=exports;
for(const platform of ['windows','macos']) {
 const version=screenshotVersion(platform),root=platform==='macos'?`macos/${version}`:version;
 const info=JSON.parse(read(`../public/app/${root}/capture-info.json`));
 assert.equal(info.Version,version);assert.match(info.ReleaseArchiveSha256,/^[a-f0-9]{64}$/i);
 if(platform==='windows')assert.equal(info.CaptureMethod,'Windows desktop client pixels');else assert.match(info.CaptureMethod,/verified published app/);
 for(const file of screenshotFiles[platform]) for(const language of ['ru','en']) {
  assert.equal(appScreenshot('es',file,platform),appScreenshot('en',file,platform));
  const image=read('../public'+appScreenshot(language,file,platform));assert.equal(image.toString('ascii',0,4),'RIFF');assert.equal(image.toString('ascii',8,12),'WEBP');
  const capture=info.Captures.find(c=>c.Language===language&&c.File===`${language}/${platform==='windows'?file.replace(/\.webp$/,'.png'):file}`);assert.ok(capture,`${platform}/${language}/${file} lacks published capture evidence`);
  assert.ok(capture.Width>=760&&capture.Height>=520);
  if(platform==='windows')assert.equal(capture.PendingVisiblePosters,0);
  else assert.equal(createHash('sha256').update(image).digest('hex'),capture.Sha256);
 }
}
console.log('Released screenshot checks passed: both operating systems, RU/EN, Spanish fallback, image formats, dimensions and published-binary provenance.');
