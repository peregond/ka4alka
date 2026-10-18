#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
arch="${1:-arm64}"
case "$arch" in arm64) swift_arch=arm64; rid=osx-arm64;; x64) swift_arch=x86_64; rid=osx-x64;; *) exit 2;; esac
version="$(python3 -c 'import xml.etree.ElementTree as e; print(e.parse("native/Kachalka.csproj").find(".//Version").text)')"
output="dist/macos-$arch"
bundle="$output/Ka4alka.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
dotnet publish desktop-host/Kachalka.DesktopHost.csproj -c Release -r "$rid" --self-contained true -p:Version="$version" -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o "$bundle/Contents/Resources/engine"
(cd macos && swift build -c release --arch "$swift_arch" --jobs 3)
bin_dir="$(cd macos && swift build -c release --arch "$swift_arch" --show-bin-path)"
cp "$bin_dir/Ka4alka" "$bundle/Contents/MacOS/Ka4alka"
cp native/Localization/en.json "$bundle/Contents/Resources/en.json"
cp update-core/update-public.pem "$bundle/Contents/Resources/update-public.pem"
python3 scripts/macos/copy-notices.py "$bundle" "$rid"
iconset="$output/AppIcon.iconset"
mkdir -p "$iconset"
for side in 16 32 128 256 512; do
  sips -z "$side" "$side" native/Assets/app-icon.png --out "$iconset/icon_${side}x${side}.png" >/dev/null
  double=$((side * 2))
  sips -z "$double" "$double" native/Assets/app-icon.png --out "$iconset/icon_${side}x${side}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/AppIcon.icns"
python3 - "$bundle" "$version" <<'PY'
import plistlib,sys
from pathlib import Path
bundle=Path(sys.argv[1]); version=sys.argv[2]
info=dict(CFBundleExecutable='Ka4alka',CFBundleIdentifier='org.ka4alka.app',CFBundleName='Ka4alka',CFBundleDisplayName='Качалка',CFBundleIconFile='AppIcon',CFBundlePackageType='APPL',CFBundleShortVersionString=version,CFBundleVersion=version,LSMinimumSystemVersion='14.0',NSHighResolutionCapable=True,NSLocalNetworkUsageDescription='Connect to your paired Windows and Mac computers to send download requests.',CFBundleURLTypes=[dict(CFBundleURLName='Ka4alka links',CFBundleURLSchemes=['magnet','kachalka'])],CFBundleDocumentTypes=[dict(CFBundleTypeName='BitTorrent',CFBundleTypeRole='Viewer',LSHandlerRank='Alternate',LSItemContentTypes=['org.bittorrent.torrent'])],UTImportedTypeDeclarations=[dict(UTTypeIdentifier='org.bittorrent.torrent',UTTypeConformsTo=['public.data'],UTTypeDescription='BitTorrent',UTTypeTagSpecification={'public.filename-extension':['torrent'],'public.mime-type':['application/x-bittorrent']})])
(bundle/'Contents/Info.plist').write_bytes(plistlib.dumps(info))
PY
# Sign nested Mach-O files first. Ad-hoc signing is intentional when an Apple
# Developer identity has not been configured; it is not Apple notarization.
find "$bundle/Contents/Resources/engine" -type f \( -name '*.dylib' -o -name 'Kachalka.Engine' -o -name 'createdump' \) -print0 | while IFS= read -r -d '' file; do codesign --force --sign - "$file"; done
codesign --force --sign - "$bundle"
# The installed bundle, ZIP and signed component catalog must describe identical
# permissions (codesign may create resource files with a restrictive umask).
python3 - "$bundle" <<'PY'
import os,sys
from pathlib import Path
for file in Path(sys.argv[1]).rglob('*'):
    if file.is_file(): file.chmod(0o755 if os.access(file,os.X_OK) else 0o644)
PY
codesign --verify --deep --strict "$bundle"
python3 scripts/macos/package.py "$bundle" "$output/Kachalka-macos-$arch.zip"
if [[ "${KACHALKA_SKIP_DMG:-0}" != 1 ]]; then
  disk="$output/disk"
  mkdir -p "$disk"
  ditto "$bundle" "$disk/Ka4alka.app"
  ln -s /Applications "$disk/Applications"
  hdiutil create -quiet -volname Ka4alka -srcfolder "$disk" -ov -format UDZO "$output/Kachalka-macos-$arch.dmg"
fi
printf '%s\n' "Built Ka4alka $version for $arch"
