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
 movies=call('catalog',dict(section='movies',page=1));assert len(movies['items'])==40
 card=movies['items'][0];call('save',dict(id=card['id']));assert any(c['id']==card['id'] for c in call('saved')['items'])
 call('hide',dict(id=card['id']));assert all(c['id']!=card['id'] for c in call('catalog',dict(section='movies',page=1))['items'])
 s=call('settings');call('unhide',dict(id=s['hidden'][0]['id']))
 assert call('setSettings',dict(uiLanguage='en'))['uiLanguage']=='en'
 assert call('downloads')['items']==[]
 assert 'text' in call('logs')
 call('shutdown');p.stdin.close();p.wait(timeout=15);assert p.returncode==0
 print(f'Bootstrap: {bootstrap_ms} ms; engine stays lazy')
 print('PASS: packaged shared-engine protocol, lazy startup, 40-card pagination, featured, save/hide, settings, logs and graceful EOF shutdown')
finally:
 if p.poll() is None:p.kill();p.wait()

 shutil.rmtree(root,ignore_errors=True)
