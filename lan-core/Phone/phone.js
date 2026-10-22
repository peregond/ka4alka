'use strict';
const en=!navigator.language.toLowerCase().startsWith('ru'),tr=(ru,eng)=>en?eng:ru,$=id=>document.getElementById(id);
document.documentElement.lang=en?'en':'ru';document.querySelectorAll('[data-ru]').forEach(el=>el.textContent=en?el.dataset.en:el.dataset.ru);
const storage={get(){try{return sessionStorage.getItem('phone-token')||'';}catch{return '';}},set(value){try{if(value)sessionStorage.setItem('phone-token',value);else sessionStorage.removeItem('phone-token');}catch{}}};
let token=storage.get(),page=1,busy=false,mode='catalog',selected=null;
// A QR carries the one-time pairing code in the fragment, never in HTTP URLs or referrers.
const qrCode=new URLSearchParams(location.hash.slice(1)).get('pair')||'';
if(location.hash)history.replaceState(null,'',location.pathname+location.search);
function message(text){$('status').textContent=text;}
function node(tag,text,cls){const el=document.createElement(tag);if(text!=null)el.textContent=String(text);if(cls)el.className=cls;return el;}
async function api(path,args={}){const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:JSON.stringify(args),cache:'no-store'});const data=await response.json();if(!response.ok){if(response.status===401&&path!='/pair')logout();throw Error(data.error||tr('Не удалось выполнить запрос','Request failed'));}return data;}
async function run(action){if(busy)return;busy=true;document.querySelectorAll('button').forEach(b=>b.disabled=true);message(tr('Загрузка…','Loading…'));try{await action();message('');}catch(error){message(error.message);}finally{busy=false;document.querySelectorAll('button').forEach(b=>b.disabled=b.dataset.unavailable==='true');}}
function connected(){ $('pair').hidden=true;$('remote').hidden=false;}
function logout(){token='';storage.set('');$('pair').hidden=false;$('remote').hidden=true;$('results').replaceChildren();}
async function pair(code){const result=await api('/pair',{code});token=result.token;storage.set(token);$('code').value='';connected();await catalog(true);}
$('pair-form').onsubmit=e=>{e.preventDefault();run(()=>pair($('code').value));};
function results(cards=false){$('results').className=cards?'cards':'';}
async function catalog(reset){mode='catalog';selected=null;$('search').hidden=false;if(reset){page=1;$('results').replaceChildren();}results(true);const data=await api('/api/catalog',{section:$('section').value,query:$('query').value,page});for(const item of data.items){const button=node('button',null,'card');const image=node('img');if(item.poster&&/^https:\/\//.test(item.poster))image.src=item.poster;image.loading='lazy';image.decoding='async';image.alt='';button.append(image,node('span',item.title+(item.year?' · '+item.year:'')));button.onclick=()=>run(()=>releases(item));$('results').append(button);} $('more').hidden=!data.hasNext;}
async function releases(item){mode='releases';selected=item;$('search').hidden=true;$('more').hidden=true;$('results').replaceChildren(node('h2',item.title));results();const data=await api('/api/releases',{id:item.id});if(!data.items.length)$('results').append(node('p',tr('Раздач не найдено. Попробуй позже.','No releases found. Try again later.')));for(const release of data.items){const row=node('article',null,'row');row.append(node('strong',release.title),node('p',[release.source,release.quality,release.audio,release.size? (release.size/1073741824).toFixed(2)+' GB':'',tr('Раздают: ','Seeds: ')+(release.seeds??'—')].filter(Boolean).join(' · ')));const button=node('button',tr('Скачать на компьютере','Download on computer'),'primary');button.disabled=!release.downloadable;button.dataset.unavailable=String(!release.downloadable);button.onclick=()=>run(async()=>{if(!confirm(tr('Скачать на компьютере?','Download on computer?')))return;await api('/api/add',{release:release.id,card:item.id});await transfers();});row.append(button);$('results').append(row);}}
async function transfers(){
 mode='downloads';$('search').hidden=true;$('more').hidden=true;results();
 const data=await api('/api/downloads');$('results').replaceChildren(node('h2',tr('Загрузки на компьютере','Downloads on computer')));
 for(const item of data.items){
  const row=node('article',null,'row'),progress=node('progress');progress.max=100;progress.value=item.progress;
  row.append(node('strong',item.title),node('p',item.status),progress);
  const action=item.filesMissing?'redownload':item.paused?'resume':'pause';
  const label=item.filesMissing?tr('Скачать заново','Download again'):item.paused?
   (item.complete?tr('Продолжить раздачу','Resume seeding'):tr('Продолжить','Resume')):
   (item.complete?tr('Остановить раздачу','Pause seeding'):tr('Пауза','Pause'));
  const button=node('button',label,item.filesMissing?'primary':'');button.dataset.action=action;
  button.disabled=!!item.busy;button.dataset.unavailable=String(!!item.busy);
  button.onclick=()=>run(async()=>{
   if(action==='redownload'&&!confirm(tr('Скачать отсутствующие файлы заново на компьютере?','Download the missing files again on your computer?')))return;
   await api('/api/control',{id:item.id,action,...(action==='redownload'?{confirmed:true}:{})});await transfers();
  });
  row.append(button);$('results').append(row);
 }
 if(!data.items.length)$('results').append(node('p',tr('Загрузок пока нет','No downloads yet')));
 const refresh=node('button',tr('Обновить','Refresh'));refresh.onclick=()=>run(transfers);$('results').append(refresh);
}
$('search').onsubmit=e=>{e.preventDefault();run(()=>catalog(true));};$('more').onclick=()=>run(async()=>{page++;try{await catalog(false);}catch(e){page--;throw e;}});$('browse').onclick=()=>run(()=>catalog(true));$('transfers').onclick=()=>run(transfers);$('logout').onclick=()=>run(async()=>{try{await api('/api/logout');}finally{logout();}});
$('magnet-form').onsubmit=e=>{e.preventDefault();run(async()=>{const source=$('magnet').value.trim();if(!source.startsWith('magnet:?'))throw Error(tr('Нужна magnet-ссылка','Enter a magnet link'));if(!confirm(tr('Скачать на компьютере?','Download on computer?')))return;await api('/api/add',{source});$('magnet').value='';await transfers();});};
if(qrCode){
 if(/^\d{6}$/.test(qrCode))run(()=>pair(qrCode));
 else message(tr('QR-код недействителен. Получи новый на компьютере.','Invalid QR code. Get a new one on your computer.'));
}else if(token){connected();run(()=>catalog(true));}
