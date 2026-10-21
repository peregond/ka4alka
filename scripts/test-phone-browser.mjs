// Install playwright and its chromium/webkit engines; dotnet build lan-tests -c Release.
import {createRequire} from 'node:module';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
const {chromium,webkit,devices}=createRequire(import.meta.url)('playwright');
const output=process.env.PHONE_SCREENSHOTS||'test-output/phone-browser';await mkdir(output,{recursive:true});
for(const [name,engine] of [['chromium',chromium],['webkit',webkit]]){
 const browser=await engine.launch({...(name==='chromium'&&process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:name==='webkit'&&process.env.WEBKIT_PATH?{executablePath:process.env.WEBKIT_PATH}:{}),args:name==='chromium'?['--no-sandbox']:[]});
 try{
  for(const locale of ['ru-RU','en-US'])for(const blockedStorage of [false,true]){
   const fixture=spawn(process.env.DOTNET_COMMAND||'dotnet',['lan-tests/bin/Release/net10.0/Kachalka.LanTests.dll','--phone-browser'],{stdio:['pipe','pipe','inherit']});
   const config=await new Promise((resolve,reject)=>{const input=createInterface({input:fixture.stdout});input.once('line',line=>{input.close();resolve(JSON.parse(line));});fixture.once('exit',code=>reject(Error('Phone fixture exited: '+code)));});
   const context=await browser.newContext({...devices['iPhone 13'],viewport:{width:390,height:844},locale});
   if(blockedStorage)await context.addInitScript(()=>{Object.defineProperty(window,'sessionStorage',{get(){throw new DOMException('Storage blocked','SecurityError');}});});
   try{
    const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('dialog',dialog=>dialog.accept());
    await page.goto(`http://127.0.0.1:${config.port}/`);
    assert.equal(await page.locator('#pair h2').textContent(),locale.startsWith('ru')?'Подключить телефон':'Connect your phone');
    await page.locator('#code').fill(config.code);await page.locator('#pair-form button').tap();await page.locator('.card').waitFor();
    assert(await page.locator('#pair').isHidden());await page.locator('.card').tap();await page.locator('.row button').waitFor();
    await page.locator('.row button').tap();await page.locator('progress').waitFor();
    assert.equal(await page.locator('progress').getAttribute('value'),'42');
    if(!blockedStorage){
     await page.screenshot({path:`${output}/phone-downloads-${name}-${locale}.png`,fullPage:true});
     await page.reload();await page.locator('.card').waitFor();
    }
    const dimensions=await page.evaluate(()=>({page:document.documentElement.scrollWidth,window:innerWidth,fonts:[...document.querySelectorAll('input,select,textarea')].map(el=>parseFloat(getComputedStyle(el).fontSize))}));
    assert(dimensions.page<=dimensions.window,'phone UI overflows horizontally');assert(dimensions.fonts.every(size=>size>=16),'iOS focus zoom triggered by small control fonts');
    await page.locator('#logout').tap();await page.locator('#pair').waitFor();assert.deepEqual(errors,[]);
   }finally{await context.close();fixture.stdin.end('\n');await new Promise(resolve=>fixture.once('exit',resolve));}
  }
  console.log(`PASS: ${name} iPhone 390×844, RU/EN, normal/blocked storage, pairing → catalog → release → download, progress, reload, touch controls and logout`);
 }finally{await browser.close();}
}
