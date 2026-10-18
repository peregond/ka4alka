"""Deterministic, permission-preserving ZIP without symlinks or AppleDouble."""
import os, stat, sys, zipfile
from pathlib import Path
bundle, target = map(Path, sys.argv[1:])
with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=False) as archive:
    for file in sorted(bundle.rglob('*')):
        if file.is_symlink(): raise ValueError(f'Symlink in app bundle: {file}')
        if not file.is_file(): continue
        info=zipfile.ZipInfo(file.relative_to(bundle.parent).as_posix(), (2026,1,1,0,0,0))
        info.compress_type=zipfile.ZIP_DEFLATED; info.create_system=3
        info.external_attr=(stat.S_IFREG | (0o755 if os.access(file,os.X_OK) else 0o644)) << 16
        archive.writestr(info,file.read_bytes(),compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
