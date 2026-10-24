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
    for appearance in ('dark','light'):
        report=json.loads((root/appearance/'checks.json').read_text())
        assert not report['failures'] and report['checks'], (language,appearance,report['failures'])
        cells=json.loads((root/appearance/'matrix.json').read_text())
        assert cells and all(cell['language']==language and cell['theme']==appearance for cell in cells)
    mapping={'featured-dark':'featured','catalog-dark':'movies','series-dark':'series','games-dark':'games-home','detail-dark':'detail-movie','downloads-dark':'downloads-empty','settings-dark':'settings','settings-light':'settings','catalog-light':'movies'}
    for name,source in mapping.items():
        appearance='light' if name.endswith('-light') else 'dark'
        with Image.open(root/appearance/f'{source}-1280x820-{appearance}-{language}.png') as image:
            image=image.convert('RGB')
            assert image.width>=760 and image.height>=520
            filename=f'screenshots-macos-{language}-{name}.webp'
            image.save(output/filename,format='WEBP',quality=88,method=6)
            info['Captures'].append(dict(File=filename,Language=language,Width=image.width,Height=image.height,Sha256=hashlib.sha256((output/filename).read_bytes()).hexdigest()))
(output/'screenshots-macos-info.json').write_text(json.dumps(info,ensure_ascii=False,indent=2)+'\n')
print('Verified Mac screenshots:',version,len(info['Captures']))
