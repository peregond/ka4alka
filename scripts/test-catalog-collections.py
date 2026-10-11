import datetime
import json
import tempfile
import unittest
from pathlib import Path
from catalog_collections import attach_collections


class CollectionsTest(unittest.TestCase):
    def test_membership_is_deduplicated_and_keeps_curated_order(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'collections.json'
            path.write_text(json.dumps([dict(id='festival', title='Фестиваль', mediaIds=['movies:b', 'movies:a', 'movies:b', 'movies:unknown'])]), encoding='utf-8')
            feed = dict(items=[dict(id='movies:a'), dict(id='movies:b')])
            attach_collections(feed, path, datetime.datetime.now(datetime.timezone.utc))
            self.assertEqual(feed['collections'][0]['mediaIds'], ['movies:b', 'movies:a'])

    def test_invalid_dates_and_duplicate_names_do_not_publish(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'collections.json'
            for definitions in [[dict(id='festival', title='F', mediaIds=[], startsAtUtc='2026-10-01T00:00:00Z', endsAtUtc='2026-09-01T00:00:00Z')], [dict(id='a', title='A'), dict(id='a', title='B')]]:
                path.write_text(json.dumps(definitions), encoding='utf-8')
                with self.assertRaises(ValueError):
                    attach_collections(dict(items=[]), path, datetime.datetime.now(datetime.timezone.utc))

    def test_utc_offsets_are_normalized_for_native_client(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'collections.json'
            path.write_text(json.dumps([dict(id='festival', title='F', mediaIds=['movies:a'], startsAtUtc='2026-10-01T00:00:00+00:00')]), encoding='utf-8')
            feed = attach_collections(dict(items=[dict(id='movies:a')]), path, datetime.datetime.now(datetime.timezone.utc))
            self.assertEqual(feed['collections'][0]['startsAtUtc'], '2026-10-01T00:00:00Z')


if __name__ == '__main__':
    unittest.main()
