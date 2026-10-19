import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const read=p=>fs.readFileSync(new URL(p,import.meta.url),'utf8');
function load(file,extra={}) {const exports={};vm.runInNewContext(ts.transpileModule(read(file),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText,{exports,URL,Response,AbortSignal,Date,Promise,...extra});return exports;}
const {detectPlatform,macArchitecture}=load('../lib/site-platform.ts');
assert.equal(detectPlatform({userAgent:'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',platform:'Win32'}),'windows');
assert.equal(detectPlatform({userAgent:'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',platform:'MacIntel',maxTouchPoints:0}),'macos');
assert.equal(detectPlatform({userAgent:'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',platform:'MacIntel',maxTouchPoints:5}),null,'iPad desktop UA is not macOS');
for(const ua of ['iPhone; CPU iPhone OS like Mac OS X','iPad; CPU OS like Mac OS X','Linux; Android 15','X11; Linux x86_64','X11; CrOS x86_64']) assert.equal(detectPlatform({userAgent:ua}),null);
assert.equal(detectPlatform({userAgent:'',userAgentData:{platform:'macOS'}}),'macos');
assert.equal(macArchitecture('MacIntel'),null,'MacIntel does not prove an Intel CPU');
assert.equal(macArchitecture('arm'),'arm64');assert.equal(macArchitecture('x86'),'x64');assert.equal(macArchitecture(undefined),null);
const version='0.44.1',tag='v'+version,base=`https://github.com/peregond/ka4alka/releases/download/${tag}/`;
const names=[`Kachalka-Setup-${version}.exe`,'Kachalka-win-x64.zip','Kachalka-macos-arm64.dmg','Kachalka-macos-x64.dmg','Kachalka-macos-arm64.zip','Kachalka-macos-x64.zip'];
for(const os of ['macos','windows']) for(const lang of ['ru','en']) for(const file of os==='macos'?['catalog-dark','series-dark','games-dark','downloads-dark','settings-dark','settings-light']:['catalog-dark','series-dark','games-dark','detail-dark','downloads-dark','catalog-light']) names.push(`screenshots-${os}-${lang}-${file}.webp`);
let requests=0;
const releaseLib=load('../lib/app-release.ts',{fetch:async(url,options)=>{requests++;assert.equal(options.redirect,'manual');return new Response(JSON.stringify({tag_name:tag,draft:false,prerelease:false,assets:names.map(name=>({name,size:100,browser_download_url:base+name}))}));}});
const replies=await Promise.all(Array.from({length:8},()=>releaseLib.getLatestRelease()));assert.equal(requests,1);
const release=replies[0];assert.equal(release.mac.arm64,base+'Kachalka-macos-arm64.dmg');assert.equal(Object.keys(release.macScreenshots).length,12);assert.equal(Object.keys(release.windowsScreenshots).length,12);
for(const unsafe of ['https://evil.test/file','http://github.com/peregond/ka4alka/releases/download/v0.44.1/file','https://github.com/other/repo/releases/download/v0.44.1/file',base+'file?token=bad']) assert.equal(releaseLib.releaseAssetUrl(unsafe,tag),null);
const route=load('../app/download/route.ts',{require:()=>({getLatestRelease:async()=>release})});
for(const [format,file] of [['installer',`Kachalka-Setup-${version}.exe`],['zip','Kachalka-win-x64.zip'],['mac-arm64','Kachalka-macos-arm64.dmg'],['mac-x64','Kachalka-macos-x64.dmg'],['mac-x64-zip','Kachalka-macos-x64.zip']]) {
 const response=await route.GET(new Request('https://ka4alka.cc/download?format='+format));assert.equal(response.status,302);assert.equal(response.headers.get('location'),base+file);assert.equal(response.headers.get('cache-control'),'no-store');
}
const choice=await route.GET(new Request('https://ka4alka.cc/download?format=mac'));assert.equal(choice.status,200);const html=await choice.text();assert.ok(html.includes(release.mac.arm64)&&html.includes(release.mac.x64));
assert.equal((await route.GET(new Request('https://ka4alka.cc/download?format=evil'))).status,400);
console.log('Platform checks passed: OS/mobile detection, truthful CPU choice, signed release URLs, both gallery sets and explicit cross-platform downloads.');
