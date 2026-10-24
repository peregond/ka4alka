"""Publish reviewed, hashed galleries without rebuilding or replacing application packages."""
import hashlib,json,re,shutil,subprocess,sys,tempfile
from pathlib import Path

version,=sys.argv[1:]
assert re.fullmatch(r'\d+\.\d+\.\d+',version)
tag='v'+version
def gh(*args):return subprocess.check_output(['gh',*args],text=True)
release=json.loads(gh('release','view',tag,'--json','isDraft,assets'))
assert not release['isDraft'],'The application must already be published'
assets={asset['name']:asset for asset in release['assets']}
uploads=[]
with tempfile.TemporaryDirectory(prefix='ka4alka-reviewed-gallery-') as temporary:
    folder=Path(temporary)
    for platform in ('windows','macos'):
        root=Path('distribution/assets')/('macos/'+version if platform=='macos' else version)
        info=json.loads((root/'capture-info.json').read_text(encoding='utf-8'))
        assert info['Version']==version and info['Platform']==platform
        archive='Kachalka-macos-arm64.zip' if platform=='macos' else 'Kachalka-win-x64.zip'
        expected=info['ReleaseArchiveSha256'].lower()
        assert re.fullmatch(r'[a-f0-9]{64}',expected)
        digest=assets[archive].get('digest')
        if digest:assert digest=='sha256:'+expected,'Gallery belongs to a different published archive'
        else:
            gh('release','download',tag,'--pattern',archive,'--dir',temporary)
            assert hashlib.sha256((folder/archive).read_bytes()).hexdigest()==expected
        base={key:value for key,value in info.items() if key!='Captures'}
        converted=[];seen=set()
        allowed={'featured-dark','catalog-dark','series-dark','games-dark','detail-dark','downloads-dark','catalog-light'}
        allowed.update({'settings-dark','settings-light'} if platform=='macos' else {'settings-lite-dark','settings-lan-dark'})
        for capture in info['Captures']:
            match=re.fullmatch(r'(ru|en)/([a-z0-9-]+)\.webp',capture['File']);assert match
            language,name=match.groups();assert name in allowed and (language,name) not in seen;seen.add((language,name))
            assert capture['Language']==language and capture['Width']>=760 and capture['Height']>=520
            if platform=='windows':assert capture['PendingVisiblePosters']==0
            source=root/capture['File'];data=source.read_bytes()
            assert data[:4]==b'RIFF' and data[8:12]==b'WEBP'
            assert hashlib.sha256(data).hexdigest()==capture['Sha256']
            target=folder/f'screenshots-{platform}-{language}-{name}.webp'
            shutil.copyfile(source,target);uploads.append(target)
            converted.append(dict(capture,File=target.name))
        required=allowed-({'settings-light'} if platform=='macos' else set())
        assert all((language,name) in seen for language in ('ru','en') for name in required)
        groups=[(f'screenshots-windows-{language}-info.json',[c for c in converted if c['Language']==language]) for language in ('ru','en')] if platform=='windows' else [('screenshots-macos-info.json',converted)]
        for name,captures in groups:
            target=folder/name;target.write_text(json.dumps(dict(base,Captures=captures),ensure_ascii=False,indent=2)+'\n',encoding='utf-8');uploads.append(target)
    assert len(uploads)==39
    gh('release','upload',tag,*map(str,uploads),'--clobber')
    readback=folder/'readback';readback.mkdir()
    gh('release','download',tag,'--pattern','screenshots-windows-*','--pattern','screenshots-macos-*','--dir',str(readback))
    for source in uploads:assert hashlib.sha256(source.read_bytes()).digest()==hashlib.sha256((readback/source.name).read_bytes()).digest()
    print('Published and read back 36 reviewed screenshots and three provenance files for',tag)
