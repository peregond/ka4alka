import hashlib,json,sys
from pathlib import Path
from PIL import Image
version,language,captures,output,*options=sys.argv[1:]
settings_only=options==['settings']
assert not options or settings_only
assert language in ('ru','en')
captures,output=map(Path,(captures,output));output.mkdir(parents=True,exist_ok=True)
info=json.loads((captures/'capture-info.json').read_text(encoding='utf-8-sig'))
assert info['Version']==version and info['Language']==language
export=dict(Version=version,Platform='windows',CaptureMethod=info['CaptureMethod'],ReleaseArchiveSha256=info['ReleaseArchiveSha256'],ReleaseDllSha256=info['ReleaseDllSha256'],Captures=[])
names=('settings-lite-dark','settings-lan-dark')
if not settings_only:
    names=('catalog-dark','series-dark','games-dark','detail-dark','downloads-dark','catalog-light')+names
for name in names:
    capture=next(item for item in info['Captures'] if item['File']==name+'.png')
    assert capture['PendingVisiblePosters']==0
    with Image.open(captures/(name+'.png')) as image:
        image=image.convert('RGB');assert image.width>=760 and image.height>=520
        filename=f'screenshots-windows-{language}-{name}.webp'
        image.save(output/filename,format='WEBP',quality=88,method=6)
        export['Captures'].append(dict(File=filename,Language=language,Width=image.width,Height=image.height,PendingVisiblePosters=0,Sha256=hashlib.sha256((output/filename).read_bytes()).hexdigest()))
metadata=f'settings-screenshots-windows-{language}-info.json' if settings_only else f'screenshots-windows-{language}-info.json'
(output/metadata).write_text(json.dumps(export,ensure_ascii=False,indent=2)+'\n')
print('Verified Windows screenshots:',version,language)
