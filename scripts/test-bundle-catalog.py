import datetime
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

from catalog_snapshot import publish_snapshot

spec = importlib.util.spec_from_file_location('bundle_catalog', Path(__file__).with_name('bundle-catalog.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class BundleCatalogTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.data = self.root / 'data'
        self.data.mkdir()
        self.configuration = self.root / 'catalog-collections.json'
        self.previous = {'seed.json': b'previous seed', 'starter.json': b'previous starter'}
        for name, payload in self.previous.items():
            (self.data / name).write_bytes(payload)
        self.configuration.write_bytes(b'previous collections')

    def snapshot(self, complete=True):
        rows = [dict(id=f'movies:movie-{index}', section='movies', title=f'Фильм {index}', year=2026,
                     pageUrl=f'https://w6.zona.plus/movies/movie-{index}') for index in range(2000 if complete else 1)]
        rows[0].update(description='Сюжет, который открывается сразу', imdb='8.2', kinopoisk=8.6,
                       people=[dict(name='Актёр', role='Актёры', pageUrl='https://w6.zona.plus/person/actor')],
                       torrentUrl='magnet:?xt=excluded')
        if complete:
            rows += [dict(id=f'series:show-{index}', section='series', title=f'Сериал {index}', year=2025,
                          pageUrl=f'https://w6.zona.plus/tvseries/show-{index}') for index in range(300)]
        feed = dict(schemaVersion=1, generatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat().replace('+00:00', 'Z'),
                    items=rows, collections=[dict(id='festival-autumn', title='  Осенний фестиваль  ',
                                                 mediaIds=['movies:movie-0', 'movies:movie-0', 'movies:unknown'],
                                                 startsAtUtc='2026-10-01T00:00:00Z', endsAtUtc='2026-11-01T00:00:00Z')])
        path = self.root / 'catalog.json'
        path.write_text(json.dumps(feed, ensure_ascii=False) + '\n', encoding='utf-8')
        manifest = publish_snapshot(path)
        return self.root / 'manifest.json', self.root / f"catalog-{manifest['revision']}.json.gz"

    def assert_previous_preserved(self):
        for name, payload in self.previous.items():
            self.assertEqual((self.data / name).read_bytes(), payload)
        self.assertEqual(self.configuration.read_bytes(), b'previous collections')

    def test_verified_snapshot_embeds_rich_metadata_starter_and_seasonal_pack(self):
        manifest, archive = self.snapshot()
        result = module.bundle_catalog(manifest, archive, self.data, self.configuration)
        rows = json.loads((self.data / 'seed.json').read_bytes())
        starter = json.loads((self.data / 'starter.json').read_bytes())
        packs = json.loads(self.configuration.read_bytes())
        self.assertEqual((result['movies'], result['series'], len(rows)), (2000, 300, 2300))
        self.assertEqual(rows[0]['description'], 'Сюжет, который открывается сразу')
        self.assertEqual(rows[0]['people'][0]['name'], 'Актёр')
        self.assertEqual(rows[0]['imdb'], '8.2')
        self.assertNotIn('torrentUrl', rows[0])
        self.assertEqual([row['id'] for row in starter], [row['id'] for row in rows[:40] + rows[2000:2040]])
        self.assertNotIn('description', starter[0])
        self.assertNotIn('people', starter[0])
        self.assertEqual(starter[0]['imdb'], '8.2')
        self.assertEqual(starter[0]['kinopoisk'], '8.6')
        self.assertEqual(packs, [dict(id='festival-autumn', title='Осенний фестиваль',
                                     startsAtUtc='2026-10-01T00:00:00Z', endsAtUtc='2026-11-01T00:00:00Z',
                                     mediaIds=['movies:movie-0'])])

    def test_corrupt_archive_preserves_every_existing_resource(self):
        manifest, archive = self.snapshot()
        compressed = bytearray(archive.read_bytes())
        compressed[-1] ^= 1
        archive.write_bytes(compressed)
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            module.bundle_catalog(manifest, archive, self.data, self.configuration)
        self.assert_previous_preserved()

    def test_near_limit_compact_snapshot_remains_bundleable(self):
        self.snapshot()
        source = self.root / 'catalog.json'
        feed = json.loads(source.read_bytes())
        feed['items'][0]['poster'] = 'https://img.test/' + 'p' * 2001
        feed['items'][0]['originalTitle'] = 'О' * 301
        for row in feed['items']:
            row['description'] = ''
        target_size = module.MAX_SNAPSHOT_BYTES - 1024
        baseline = module._refresh.snapshot_bytes(feed)
        # Distribute Unicode descriptions across real catalog rows, keeping
        # individual descriptions small while exercising the actual 32 MiB cap.
        characters = (target_size - len(baseline)) // (2 * len(feed['items']))
        for row in feed['items']:
            row['description'] = 'я' * characters
        payload = module._refresh.snapshot_bytes(feed)
        feed['items'][-1]['description'] += 'x' * (target_size - len(payload))
        payload = module._refresh.snapshot_bytes(feed)
        self.assertEqual(len(payload), target_size)
        self.assertGreater(len(json.dumps(feed['items'], ensure_ascii=False, indent=2).encode('utf-8')),
                           module.MAX_SNAPSHOT_BYTES)
        source.write_bytes(payload)
        manifest = publish_snapshot(source)
        archive = self.root / f"catalog-{manifest['revision']}.json.gz"
        module.bundle_catalog(self.root / 'manifest.json', archive, self.data, self.configuration)
        bundled = (self.data / 'seed.json').read_bytes()
        self.assertGreater(len(bundled), module.MAX_SNAPSHOT_BYTES - 64 * 1024)
        self.assertLessEqual(len(bundled), module.MAX_SNAPSHOT_BYTES)
        self.assertTrue(bundled.endswith(b'\n'))
        rows = json.loads(bundled)
        self.assertEqual(len(rows), 2300)
        self.assertEqual(rows[0]['description'], feed['items'][0]['description'])
        self.assertEqual(rows[0]['people'][0]['name'], 'Актёр')
        starter_payload = (self.data / 'starter.json').read_bytes()
        self.assertLess(len(starter_payload), module.MAX_STARTER_BYTES)
        starter = json.loads(starter_payload)
        self.assertEqual([row['id'] for row in starter], [row['id'] for row in rows[:40] + rows[2000:2040]])
        self.assertNotIn('description', starter[0])
        self.assertNotIn('people', starter[0])
        self.assertNotIn('poster', starter[0])
        self.assertNotIn('originalTitle', starter[0])
        self.assertEqual(rows[0]['poster'], feed['items'][0]['poster'])
        self.assertEqual(rows[0]['originalTitle'], feed['items'][0]['originalTitle'])

    def test_valid_but_incomplete_snapshot_preserves_every_existing_resource(self):
        manifest, archive = self.snapshot(complete=False)
        with self.assertRaisesRegex(ValueError, 'incomplete'):
            module.bundle_catalog(manifest, archive, self.data, self.configuration)
        self.assert_previous_preserved()


if __name__ == '__main__':
    unittest.main()
