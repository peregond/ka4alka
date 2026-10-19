"""Export screenshots from the signed published app, without modifying its binary."""
import hashlib,json,plistlib,re,sys
from pathlib import Path
from PIL import Image
version,published,captures,output=sys.argv[1:]
assert re.fullmatch(r'\d+\.\d+\.\d+',version)
published,captures,output=map(Path,(published,captures,output))
assert plistlib.loads((published/'app/Ka4alka.app/Contents/Info.plist').read_bytes())['CFBundleShortVersionString']==version
output.mkdir(parents=True,exist_ok=True)
archive=published/'Kachalka-macos-arm64.zip'
info=dict(Version=version,Platform='macos',CaptureMethod='Native AppKit window pixels from verified published app',ReleaseArchiveSha256=hashlib.sha256(archive.read_bytes()).hexdigest(),Captures=[])
for language in ('ru','en'):
    root=captures/language
    assert len(json.loads((root/'checks.json').read_text()))>=15
    # The released diagnostic driver names its early screenshots *-ru, but
    # these stages use the supplied profile language until Settings scenarios.
    mapping={'catalog-dark':'movies-ru','series-dark':'series-ru','games-dark':'game-home-ru','downloads-dark':'downloads-ru','settings-dark':f'settings-{language}-dark-wide','settings-light':f'settings-{language}-light-wide'}
    for name,source in mapping.items():
        with Image.open(root/(source+'.png')) as image:
            image=image.convert('RGB')
            assert image.width>=760 and image.height>=520
            filename=f'screenshots-macos-{language}-{name}.webp'
            image.save(output/filename,format='WEBP',quality=88,method=6)
            info['Captures'].append(dict(File=filename,Language=language,Width=image.width,Height=image.height,Sha256=hashlib.sha256((output/filename).read_bytes()).hexdigest()))
(output/'screenshots-macos-info.json').write_text(json.dumps(info,ensure_ascii=False,indent=2)+'\n')
print('Verified Mac screenshots:',version,len(info['Captures']))
