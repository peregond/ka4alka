import datetime
import gzip
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from catalog_snapshot import publish_snapshot, restore_snapshot, snapshot_asset_name, RELEASE_BASE


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path = Path(self.folder.name) / 'catalog.json'
        self.raw = (json.dumps(dict(schemaVersion=1, generatedAtUtc='2026-10-01T05:00:00Z',
                                   items=[dict(title='Кино', description='Текст без изображений')]), ensure_ascii=False) + '\n').encode('utf-8')
        self.path.write_bytes(self.raw)

    def test_immutable_archive_manifest_round_trip(self):
        manifest = publish_snapshot(self.path)
        revision = hashlib.sha256(self.raw).hexdigest()
        archive = self.path.parent / f'catalog-{revision}.json.gz'
        compressed = archive.read_bytes()
        self.assertEqual(gzip.decompress(compressed), self.raw)
        self.assertEqual(compressed[4:8], b'\0' * 4)
        self.assertEqual(compressed[3], 0, 'gzip does not contain a local filename')
        self.assertEqual(manifest, json.loads((self.path.parent / 'manifest.json').read_bytes()))
        self.assertEqual(manifest['revision'], revision)
        self.assertEqual(manifest['sha256'], hashlib.sha256(compressed).hexdigest())
        self.assertEqual(manifest['downloadUrl'], RELEASE_BASE + archive.name)
        self.assertEqual(manifest['bytes'], len(compressed))
        self.assertEqual(manifest['uncompressedBytes'], len(self.raw))
        self.assertEqual(self.path.read_bytes(), self.raw)
        before = archive.stat().st_mtime_ns
        self.assertEqual(publish_snapshot(self.path), manifest)
        self.assertEqual(archive.stat().st_mtime_ns, before, 'existing immutable revision is not overwritten')

    def test_new_revision_keeps_old_asset_and_replaces_manifest(self):
        previous = publish_snapshot(self.path)
        self.path.write_bytes(self.raw.replace('Кино'.encode(), 'Другое кино'.encode()))
        current = publish_snapshot(self.path)
        self.assertNotEqual(current['revision'], previous['revision'])
        self.assertTrue((self.path.parent / previous['downloadUrl'].split('/')[-1]).is_file())
        self.assertEqual(json.loads((self.path.parent / 'manifest.json').read_bytes()), current)

    def test_failed_publication_preserves_previous_manifest(self):
        previous = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        before = manifest_path.read_bytes()
        for raw in [b'', b'[]', b'{"schemaVersion":2}', self.raw.replace(b'2026-10-01T05:00:00Z', b'2026-10-01T05:00:00')]:
            self.path.write_bytes(raw)
            with self.assertRaises((ValueError, json.JSONDecodeError)):
                publish_snapshot(self.path)
            self.assertEqual(manifest_path.read_bytes(), before)
        self.path.write_bytes(self.raw)
        archive = self.path.parent / previous['downloadUrl'].split('/')[-1]
        archive.write_bytes(b'different immutable bytes')
        with self.assertRaisesRegex(ValueError, 'Immutable'):
            publish_snapshot(self.path)
        self.assertEqual(manifest_path.read_bytes(), before)

    def test_oversized_feed_does_not_publish(self):
        with patch('catalog_snapshot.MAX_SNAPSHOT_BYTES', len(self.raw) - 1):
            with self.assertRaisesRegex(ValueError, 'limit'):
                publish_snapshot(self.path)
        self.assertFalse((self.path.parent / 'manifest.json').exists())

    def test_restore_works_when_mutable_alias_is_missing(self):
        manifest = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        asset_name = snapshot_asset_name(manifest_path)
        archive = self.path.parent / asset_name
        self.path.unlink()
        restored = self.path.parent / 'restored' / 'catalog.json'
        self.assertEqual(restore_snapshot(manifest_path, archive, restored), manifest)
        self.assertEqual(restored.read_bytes(), self.raw)
        self.assertFalse(self.path.exists(), 'restoration does not depend on the legacy alias')

    def test_restore_cli_prints_only_asset_name_and_restores(self):
        manifest = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        archive = self.path.parent / manifest['downloadUrl'].split('/')[-1]
        command = [sys.executable, str(Path(__file__).with_name('catalog_snapshot.py'))]
        result = subprocess.run(command + ['--asset-name', str(manifest_path)], capture_output=True, text=True, check=True)
        self.assertEqual(result.stdout, archive.name + '\n')
        self.assertEqual(result.stderr, '')
        restored = self.path.parent / 'restored.json'
        result = subprocess.run(command + ['--restore', str(manifest_path), str(archive), str(restored)], capture_output=True, text=True, check=True)
        self.assertEqual(result.stdout, '')
        self.assertEqual(result.stderr, '')
        self.assertEqual(restored.read_bytes(), self.raw)

    def test_invalid_restore_manifest_preserves_previous_feed(self):
        original = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        archive = self.path.parent / original['downloadUrl'].split('/')[-1]
        mutations = [dict(schemaVersion=True), dict(schemaVersion=2), dict(revision='a' * 63),
                     dict(sha256='ABCDEF' * 10 + 'AAAA'), dict(downloadUrl='https://evil.invalid/catalog.json.gz'),
                     dict(downloadUrl=original['downloadUrl'] + '?x=1'), dict(bytes=True), dict(bytes=0),
                     dict(bytes=32 * 1024 * 1024 + 1), dict(uncompressedBytes=0),
                     dict(uncompressedBytes=32 * 1024 * 1024 + 1), dict(generatedAtUtc='2026-10-01T05:00:00')]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                manifest_path.write_text(json.dumps({**original, **mutation}), encoding='utf-8')
                with self.assertRaises(ValueError):
                    snapshot_asset_name(manifest_path)
                with self.assertRaises(ValueError):
                    restore_snapshot(manifest_path, archive, self.path)
                self.assertEqual(self.path.read_bytes(), self.raw)
        for raw in [b'{', b'[]', b'{"schemaVersion":1,"schemaVersion":1}', b' ' * (64 * 1024 + 1)]:
            manifest_path.write_bytes(raw)
            with self.assertRaises(ValueError):
                restore_snapshot(manifest_path, archive, self.path)
            self.assertEqual(self.path.read_bytes(), self.raw)

    def test_invalid_restore_archive_preserves_previous_feed(self):
        original = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        archive = self.path.parent / original['downloadUrl'].split('/')[-1]
        valid = archive.read_bytes()
        invalid_archives = [valid[:-1], valid[:10] + bytes([valid[10] ^ 1]) + valid[11:], valid[:-8]]
        for compressed in invalid_archives:
            with self.subTest(size=len(compressed)):
                archive.write_bytes(compressed)
                with self.assertRaises(ValueError):
                    restore_snapshot(manifest_path, archive, self.path)
                self.assertEqual(self.path.read_bytes(), self.raw)
        # Even a manifest with a matching compressed digest cannot authorize a
        # truncated gzip or a raw body with a different revision.
        for compressed in [valid[:-8], gzip.compress(self.raw + b' ', mtime=0)]:
            archive.write_bytes(compressed)
            manifest_path.write_text(json.dumps({**original, 'bytes': len(compressed),
                'sha256': hashlib.sha256(compressed).hexdigest()}), encoding='utf-8')
            with self.assertRaises(ValueError):
                restore_snapshot(manifest_path, archive, self.path)
            self.assertEqual(self.path.read_bytes(), self.raw)

    def test_restore_gzip_expansion_and_raw_hash_are_bounded(self):
        original = publish_snapshot(self.path)
        manifest_path = self.path.parent / 'manifest.json'
        archive = self.path.parent / original['downloadUrl'].split('/')[-1]
        for raw in [b'0' * (4 * 1024 * 1024), self.raw.replace(b'items', b'ITEMS')]:
            compressed = gzip.compress(raw, mtime=0)
            archive.write_bytes(compressed)
            manifest_path.write_text(json.dumps({**original, 'bytes': len(compressed),
                'sha256': hashlib.sha256(compressed).hexdigest()}), encoding='utf-8')
            with self.assertRaises(ValueError):
                restore_snapshot(manifest_path, archive, self.path)
            self.assertEqual(self.path.read_bytes(), self.raw)


if __name__ == '__main__':
    unittest.main()
