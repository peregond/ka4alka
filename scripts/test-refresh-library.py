import importlib.util
import json
import gzip
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('refresh_library', Path(__file__).with_name('refresh-library.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CollectorTests(unittest.TestCase):
    @staticmethod
    def item(slug, **metadata):
        return dict(id='movies:' + slug, section='movies', title='Тест', year=2026,
                    pageUrl='https://w6.zona.plus/movies/' + slug, **metadata)

    def test_real_card_and_checked_links(self):
        card = '''<li class="results-item-wrap"><a itemprop="url" href="/movies/test-film"><span itemprop="name">Тест &amp; кино</span></a><span class="results-item-year">2026</span><meta itemprop="image" content="https://img1.zonapic.com/images/test.jpg"></li>'''
        row = module.parse_cards(card, 'movies', 'movies')[0]
        self.assertEqual(row['title'], 'Тест & кино')
        self.assertEqual(row['year'], 2026)
        self.assertEqual(row['id'], 'movies:test-film')
        self.assertEqual(row['pageUrl'], 'https://w6.zona.plus/movies/test-film')
        self.assertEqual(module.parse_cards(card, 'series', 'tvseries'), [])
        self.assertIsNone(module.parse_cards(card.replace('https://img1.zonapic.com/', 'http://untrusted.test/'), 'movies', 'movies')[0]['poster'])
        self.assertEqual(module.parse_cards(card.replace('/movies/test-film', '/movies/..'), 'movies', 'movies'), [])

    def test_recent_pages_do_not_discard_older_library_or_rich_details(self):
        older = self.item('old', description='Сохранённый сюжет', people=[dict(name='Актёр', role='Актёры', pageUrl='')])
        refreshed = self.item('fresh', description='Описание', imdb='7.5', imdbId='tt1234567')
        rows = module.merge_catalog([older, refreshed], [self.item('fresh', description=None, imdb='—'), self.item('new')])
        indexed = {row['id']: row for row in rows}
        self.assertEqual(set(indexed), {'movies:old', 'movies:fresh', 'movies:new'})
        self.assertEqual(indexed['movies:old']['people'], older['people'])
        self.assertEqual(indexed['movies:fresh']['imdb'], '7.5')
        self.assertEqual(indexed['movies:fresh']['description'], 'Описание')

    def test_changed_identity_does_not_inherit_other_movie_credits(self):
        prior = self.item('remake', description='Сюжет прежней версии', imdbId='tt1234567')
        fresh = {**self.item('remake'), 'year': 2025}
        row = module.merge_catalog([prior], [fresh])[0]
        self.assertNotIn('description', row)
        self.assertNotIn('imdbId', row)

    def test_malformed_optional_fields_cannot_poison_following_metadata_refresh(self):
        for value in [None, False, 0, [], {}, '', 'invalid']:
            with self.subTest(value=value):
                restored = module.checked_item(self.item('known', imdbId=value, people=None, genreKeys=None, countryKeys=False))
                self.assertNotIn('imdbId', restored)
                self.assertNotIn('people', restored)
                self.assertNotIn('genreKeys', restored)
                self.assertNotIn('countryKeys', restored)
        self.assertEqual(module.checked_item(self.item('known', imdbId='tt1234567'))['imdbId'], 'tt1234567')

    def test_previous_download_must_be_valid_and_keeps_only_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'catalog.json'
            row = self.item('known', torrentUrl='magnet:?xt=bad', releases=[{'title': 'Раздача'}])
            path.write_text(json.dumps({'schemaVersion': 1, 'items': [row]}), encoding='utf-8')
            restored = module.load_previous(path)[0]
            self.assertNotIn('torrentUrl', restored)
            self.assertNotIn('releases', restored)
            path.write_text(json.dumps({'schemaVersion': 1, 'items': [self.item('../bad')]}), encoding='utf-8')
            with self.assertRaises(ValueError):
                module.load_previous(path)

    def test_snapshot_validation_cannot_publish_a_tiny_or_oversized_library(self):
        with self.assertRaisesRegex(ValueError, 'incomplete'):
            module.snapshot_bytes({'items': [self.item('single')]})
        rows = [self.item('movie-' + str(index)) for index in range(2000)]
        rows += [dict(id='series:show-' + str(index), section='series', title='Сериал', year=2026,
                      pageUrl='https://w6.zona.plus/tvseries/show-' + str(index)) for index in range(300)]
        payload = module.snapshot_bytes({'schemaVersion': 1, 'items': rows})
        self.assertEqual(len(json.loads(payload)['items']), 2300)
        with patch.object(module, 'MAX_SNAPSHOT_BYTES', 64):
            with self.assertRaisesRegex(ValueError, 'limit'):
                module.snapshot_bytes({'items': rows})

    def test_producer_retains_previous_library_and_emits_a_readable_compressed_snapshot(self):
        rows = [self.item('movie-' + str(index)) for index in range(2000)]
        rows[0]['description'] = 'Сохранённое описание'
        rows += [dict(id='series:show-' + str(index), section='series', title='Сериал', year=2026,
                      pageUrl='https://w6.zona.plus/tvseries/show-' + str(index)) for index in range(300)]
        with tempfile.TemporaryDirectory() as directory:
            previous, target = Path(directory) / 'previous.json', Path(directory) / 'catalog.json'
            bundled = Path(directory) / 'seed.json'
            bundled.write_text(json.dumps([self.item('primary-only'), self.item('movie-0', originalTitle='Original title', description='Старый сюжет')]), encoding='utf-8')
            previous.write_text(json.dumps({'schemaVersion': 1, 'items': rows}), encoding='utf-8')
            with patch('sys.argv', ['refresh-library.py', '--previous', str(previous), '--output', str(target)]), \
                 patch.object(module, 'BUNDLED_CATALOG_PATH', bundled), \
                 patch.object(module, 'collect', return_value=[self.item('new')]), \
                 patch('library_metadata.enrich', side_effect=lambda current, **kwargs: current):
                module.main()
            feed = json.loads(target.read_bytes())
            self.assertEqual(len(feed['items']), 2302)
            self.assertTrue(any(row['id'] == 'movies:primary-only' for row in feed['items']))
            restored = next(row for row in feed['items'] if row['id'] == 'movies:movie-0')
            self.assertEqual(restored['description'], 'Сохранённое описание')
            self.assertEqual(restored['originalTitle'], 'Original title')
            snapshots = list(Path(directory).glob('catalog-*.json.gz'))
            self.assertEqual(len(snapshots), 1)
            self.assertEqual(gzip.decompress(snapshots[0].read_bytes()), target.read_bytes())
            self.assertTrue((Path(directory) / 'manifest.json').exists())
            target.write_bytes(b'last successful catalog')
            with patch('sys.argv', ['refresh-library.py', '--previous', str(previous), '--output', str(target)]), \
                 patch.object(module, 'BUNDLED_CATALOG_PATH', bundled), \
                 patch.object(module, 'collect', side_effect=OSError('blocked source')):
                with self.assertRaises(OSError):
                    module.main()
            self.assertEqual(target.read_bytes(), b'last successful catalog')


if __name__ == '__main__':
    unittest.main()
