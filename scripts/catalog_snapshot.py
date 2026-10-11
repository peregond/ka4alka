"""Build and restore immutable compressed text snapshots from release manifests."""
import argparse
import datetime
import gzip
import hashlib
import io
import json
import os
import re
import struct
import tempfile
import zlib
from pathlib import Path

MAX_SNAPSHOT_BYTES = 32 * 1024 * 1024
MAX_MANIFEST_BYTES = 64 * 1024
RELEASE_BASE = 'https://github.com/peregond/ka4alka/releases/download/catalog-data/'


def _read_bounded(path, maximum, label):
    with Path(path).open('rb') as handle:
        raw = handle.read(maximum + 1)
    if not raw or len(raw) > maximum:
        raise ValueError(f'{label} exceeds the supported limit or is empty')
    return raw


def _unique_fields(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f'Duplicate JSON field: {key}')
        result[key] = value
    return result


def _json(raw):
    def invalid_constant(value):
        raise ValueError(f'Invalid JSON constant: {value}')
    return json.loads(raw, object_pairs_hook=_unique_fields, parse_constant=invalid_constant)


def _check_timestamp(generated):
    if not isinstance(generated, str) or not re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z', generated):
        raise ValueError('Invalid catalog UTC timestamp')
    try:
        timestamp = datetime.datetime.fromisoformat(generated.replace('Z', '+00:00'))
    except ValueError as error:
        raise ValueError('Invalid catalog timestamp') from error
    if timestamp.year < 2020 or timestamp > datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(minutes=15):
        raise ValueError('Invalid catalog UTC timestamp')


def _check_feed(raw):
    feed = _json(raw)
    if not isinstance(feed, dict) or type(feed.get('schemaVersion')) is not int or feed['schemaVersion'] != 1:
        raise ValueError('Unsupported text catalog schema')
    _check_timestamp(feed.get('generatedAtUtc'))
    return feed


def _read_manifest(manifest_path):
    manifest = _json(_read_bounded(manifest_path, MAX_MANIFEST_BYTES, 'Catalog manifest'))
    if not isinstance(manifest, dict) or type(manifest.get('schemaVersion')) is not int or manifest['schemaVersion'] != 1:
        raise ValueError('Unsupported catalog manifest schema')
    for key in ['revision', 'sha256']:
        if not isinstance(manifest.get(key), str) or not re.fullmatch(r'[0-9a-f]{64}', manifest[key]):
            raise ValueError('Invalid catalog manifest hash')
    _check_timestamp(manifest.get('generatedAtUtc'))
    name = f"catalog-{manifest['revision']}.json.gz"
    if manifest.get('downloadUrl') != RELEASE_BASE + name:
        raise ValueError('Untrusted catalog snapshot URL')
    for key, minimum in [('bytes', 18), ('uncompressedBytes', 1)]:
        if type(manifest.get(key)) is not int or not minimum <= manifest[key] <= MAX_SNAPSHOT_BYTES:
            raise ValueError('Catalog manifest size exceeds the supported limit')
    return manifest, name


def snapshot_asset_name(manifest_path):
    """Return a trusted basename suitable for downloading a release asset."""
    return _read_manifest(manifest_path)[1]


def _replace_bytes(path, payload):
    """Readers see either the complete previous file or the complete new file."""
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix=path.name + '.', suffix='.tmp', delete=False) as handle:
            temporary = Path(handle.name)
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def publish_snapshot(feed_path):
    """Create snapshot assets beside a validated feed, without changing its bytes."""
    path = Path(feed_path)
    raw = _read_bounded(path, MAX_SNAPSHOT_BYTES, 'Text snapshot')
    feed = _check_feed(raw)
    generated = feed.get('generatedAtUtc')
    revision = hashlib.sha256(raw).hexdigest()
    archive = io.BytesIO()
    # GzipFile avoids platform-dependent OS bytes from gzip.compress(mtime=0).
    with gzip.GzipFile(filename='', mode='wb', fileobj=archive, compresslevel=9, mtime=0) as handle:
        handle.write(raw)
    compressed = archive.getvalue()
    if len(compressed) > MAX_SNAPSHOT_BYTES:
        raise ValueError('Compressed text snapshot exceeds the supported limit')
    name = f'catalog-{revision}.json.gz'
    target = path.parent / name
    if target.exists():
        if target.read_bytes() != compressed:
            raise ValueError('Immutable catalog revision already has different bytes')
    else:
        _replace_bytes(target, compressed)
    manifest = dict(schemaVersion=1, revision=revision, generatedAtUtc=generated,
                    downloadUrl=RELEASE_BASE + name,
                    sha256=hashlib.sha256(compressed).hexdigest(), bytes=len(compressed),
                    uncompressedBytes=len(raw))
    _replace_bytes(path.parent / 'manifest.json', (json.dumps(manifest, separators=(',', ':')) + '\n').encode('utf-8'))
    return manifest


def restore_snapshot(manifest_path, gzip_path, target_path):
    """Recover the exact previous feed using the authoritative immutable assets."""
    manifest, _ = _read_manifest(manifest_path)
    compressed = _read_bounded(gzip_path, MAX_SNAPSHOT_BYTES, 'Compressed text snapshot')
    if len(compressed) != manifest['bytes'] or hashlib.sha256(compressed).hexdigest() != manifest['sha256']:
        raise ValueError('Compressed catalog size or hash mismatch')
    if compressed[:3] != b'\x1f\x8b\x08' or compressed[3] & 0xe0:
        raise ValueError('Invalid catalog gzip header')
    try:
        with gzip.GzipFile(fileobj=io.BytesIO(compressed), mode='rb') as handle:
            raw = handle.read(manifest['uncompressedBytes'] + 1)
    except (EOFError, gzip.BadGzipFile, zlib.error) as error:
        raise ValueError('Invalid or truncated catalog gzip') from error
    if len(raw) != manifest['uncompressedBytes']:
        raise ValueError('Uncompressed catalog size exceeds the declared limit or is incomplete')
    checksum, size = struct.unpack('<II', compressed[-8:])
    if size != len(raw) or checksum != zlib.crc32(raw):
        raise ValueError('Invalid catalog gzip trailer')
    if hashlib.sha256(raw).hexdigest() != manifest['revision']:
        raise ValueError('Catalog revision does not match restored content')
    feed = _check_feed(raw)
    if feed['generatedAtUtc'] != manifest['generatedAtUtc']:
        raise ValueError('Catalog timestamp does not match its manifest')
    target = Path(target_path)
    target.parent.mkdir(parents=True, exist_ok=True)
    _replace_bytes(target, raw)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument('--asset-name', metavar='MANIFEST', help='Print only the validated immutable asset filename')
    action.add_argument('--restore', nargs=3, metavar=('MANIFEST', 'GZIP', 'TARGET'), help='Validate and atomically restore a prior catalog')
    args = parser.parse_args()
    try:
        if args.asset_name:
            print(snapshot_asset_name(args.asset_name))
        else:
            restore_snapshot(*args.restore)
    except (OSError, ValueError) as error:
        parser.error(str(error))


if __name__ == '__main__':
    main()
