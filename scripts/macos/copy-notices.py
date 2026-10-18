"""Include notices for the exact shared dependencies and bundled .NET runtime."""
import json, shutil, sys
from pathlib import Path

bundle, rid = sys.argv[1:]
assets = json.loads(Path('desktop-host/obj/project.assets.json').read_text())
roots = [Path(p) for p in assets['packageFolders']]
licenses = Path(bundle) / 'Contents/Resources/licenses'
licenses.mkdir(parents=True, exist_ok=True)
packages = [name.lower() for name, value in assets['libraries'].items() if value['type'] == 'package']
# Runtime packs are download dependencies, not ordinary package references.
for root in roots:
    packages += [str(p.relative_to(root)) for p in (root / ('microsoft.netcore.app.runtime.' + rid)).glob('*') if p.is_dir()]
for package in sorted(set(packages)):
    for root in roots:
        source = root / package
        if not source.is_dir(): continue
        for file in source.iterdir():
            name = file.name.upper().replace('-', '')
            if file.is_file() and (name.startswith('LICENSE') or name.startswith('THIRDPARTYNOTICES')):
                shutil.copyfile(file, licenses / (package.replace('/', '-') + '-' + file.name))
shutil.copyfile('distribution/licenses/HtmlAgilityPack-LICENSE.txt', licenses / 'HtmlAgilityPack-LICENSE.txt')
if not any(licenses.glob('monotorrent-*-LICENSE*')) or not any(licenses.glob('microsoft.netcore.app.runtime.*-LICENSE*')):
    raise RuntimeError('Required engine or runtime license notice is missing')
