import json,os,subprocess,tempfile,time,queue,threading,sys,shutil
from pathlib import Path
root=Path(tempfile.mkdtemp(prefix='ka4alka-mac-engine-'))
(root/'downloads').mkdir(); (root/'settings.json').write_text(json.dumps(dict(Folder=str(root/'downloads'),FolderConfigured=True,AutoResumeDownloads=False,CheckForUpdates=False,AutoUpdate=False)))
env=os.environ.copy();env['KACHALKA_DATA']=str(root);env['DOTNET_CLI_TELEMETRY_OPTOUT']='1'
p=subprocess.Popen(sys.argv[1:] or ['dotnet','desktop-host/bin/Release/net10.0/Kachalka.Engine.dll'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,env=env)
messages=queue.Queue()
threading.Thread(target=lambda:[messages.put(line) for line in p.stdout],daemon=True).start()
def call(method,params=None):
 token=str(time.monotonic_ns());p.stdin.write(json.dumps(dict(id=token,method=method,params=params or {}))+'\n');p.stdin.flush()
 deadline=time.monotonic()+60
 while time.monotonic()<deadline:
  try:line=messages.get(timeout=1)
  except queue.Empty:continue
  if not line:raise RuntimeError('engine exited: '+p.stderr.read()[-2000:])
  msg=json.loads(line)
  if msg.get('id')==token:
   if 'error' in msg:raise RuntimeError(str(msg['error']))
   return msg['result']
 raise TimeoutError(method)
try:
 started=time.monotonic();boot=call('bootstrap');bootstrap_ms=round((time.monotonic()-started)*1000);assert boot['engineCreated'] is False
 assert boot['version']==__import__('xml.etree.ElementTree',fromlist=['parse']).parse('native/Kachalka.csproj').find('.//Version').text
 featured=call('featured');assert featured['groups']
 for group in featured['groups']:
  assert call('featured',dict(id=group['id']))['items']
 movies=call('catalog',dict(section='movies',page=1));assert len(movies['items'])==40
 card=movies['items'][0];call('save',dict(id=card['id']));assert any(c['id']==card['id'] for c in call('saved')['items'])
 call('hide',dict(id=card['id']));assert all(c['id']!=card['id'] for c in call('catalog',dict(section='movies',page=1))['items'])
 s=call('settings');call('unhide',dict(id=s['hidden'][0]['id']))
 reported_link='kachalka://movie/odnazhdy-na-blizhnem-vostoke?title=%D0%9E%D0%B4%D0%BD%D0%B0%D0%B6%D0%B4%D1%8B%2520%D0%BD%D0%B0%2520%D0%91%D0%BB%D0%B8%D0%B6%D0%BD%D0%B5%D0%BC%2520%D0%92%D0%BE%D1%81%D1%82%D0%BE%D0%BA%D0%B5&year=2026'
 linked=call('openLink',dict(url=reported_link))
 assert linked['title']=='Однажды на Ближнем Востоке' and linked['year']==2026,linked
 from urllib.parse import urlparse,parse_qs
 assert parse_qs(urlparse(call('share',dict(id=linked['id']))['url']).query)['title']==['Однажды на Ближнем Востоке']
 print('PASS: reported double-escaped share title resolves through exact catalog page metadata and re-shares canonically')
 potter_years=[2001,2002,2004,2005,2007,2009,2010,2011]
 potter_ids={'tt0241527','tt0295297','tt0304141','tt0330373','tt0373889','tt0417741','tt0926084','tt1201607'}
 for language,query in [('ru','гарри поттер'),('en','Harry Potter')]:
  call('setSettings',dict(uiLanguage=language))
  found=call('catalog',dict(section='movies',query=query))['items']
  parts=[c for c in found if c['section']=='movies' and c.get('imdbId') in potter_ids]
  assert sorted(c['year'] for c in parts)==potter_years,(language,[(c['title'],c['year']) for c in parts])
  assert len({c['id'] for c in parts})==8
  assert {c['imdbId'] for c in parts}==potter_ids
  assert all(c['title'].startswith('Гарри Поттер' if language=='ru' else 'Harry Potter') for c in parts)
 print('PASS: fresh shared-engine Russian and English searches return all eight Potter films with localized titles and distinct identities')
 assert call('setSettings',dict(uiLanguage='en'))['uiLanguage']=='en'
 assert call('downloads')['items']==[]
 assert call('phone',dict(action='state'))['running'] is False
 for _ in range(2):
  phone=call('phone',dict(action='start'));assert phone['running'] and len(phone['state']['code'])==6
  assert call('phone',dict(action='state'))['running'] is True
  assert call('phone',dict(action='stop'))['running'] is False
 assert call('downloads')['items']==[]
 assert 'text' in call('logs')
 call('shutdown');p.stdin.close();p.wait(timeout=15)
 assert p.returncode==0, f'engine shutdown exited {p.returncode}: {p.stderr.read()[-4000:]}'
 print(f'Bootstrap: {bootstrap_ms} ms; engine stays lazy')
 print('PASS: packaged shared-engine protocol, lazy startup, 40-card pagination, featured, save/hide, settings, logs and graceful EOF shutdown')
finally:
 if p.poll() is None:p.kill();p.wait()

 shutil.rmtree(root,ignore_errors=True)
