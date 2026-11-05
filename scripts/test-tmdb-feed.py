import datetime
import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location('feed', pathlib.Path(__file__).with_name('probe-tmdb-feed.py'))
feed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feed)


class FeedTests(unittest.TestCase):
    def test_library_title_maps_through_imdb_id_exactly_once(self):
        row = dict(section='series', imdbId='tt1')
        self.assertEqual(feed.tmdb_id(row, 'k', lambda *a, **k: {'tv_results': [{'id': 7}]}), 7)
        self.assertIsNone(feed.tmdb_id(row, 'k', lambda *a, **k: {'tv_results': [{'id': 7}, {'id': 8}]}))
        self.assertIsNone(feed.tmdb_id(dict(section='movies'), 'k', lambda *a, **k: self.fail('no id, no request')))

    def test_popular_list_follows_pages_until_the_last(self):
        calls = []
        def fetch(path, token, **params):
            calls.append((path, params['page'], params.get('primary_release_date.gte')))
            return {'results': [{'id': params['page'], 'title': f"T{params['page']}", 'original_title': 'O'}], 'total_pages': 2}
        got = feed.popular_recent('movies', 'k', datetime.date(2025, 1, 1), 10, fetch)
        self.assertEqual([item[0] for item in got], [1, 2])
        self.assertEqual(calls[0], ('/discover/movie', 1, '2025-01-01'))
        tv = []
        feed.popular_recent('series', 'k', datetime.date(2025, 1, 1), 1, lambda p, t, **k: tv.append((p, sorted(k))) or {'results': []})
        self.assertEqual(tv[0][0], '/discover/tv')
        self.assertIn('first_air_date.gte', tv[0][1])

    def test_feed_report_counts_what_the_library_has_and_lacks(self):
        top = [(1, 'А', 'A'), (2, 'Б', 'B'), (3, 'В', 'C'), (4, 'Г', 'D')]
        result = feed.feed_report('movies', {1, 3}, top)
        self.assertEqual((result['top'], result['inside'], result['share']), (4, 2, 50))
        self.assertEqual(result['first_missing'], ['Б / B', 'Г / D'])

    def test_names_and_people(self):
        row = dict(title='Тест')
        card = dict(title='Тест', original_title='Test', credits={'cast': [
            dict(name='Иван', profile_path='/a.jpg'), dict(name='John', profile_path=None), dict(name='Max', profile_path='/b.jpg')]})
        part = feed.names_and_people(row, card)
        self.assertEqual((part['has_ru_title'], part['same_as_library'], part['translated']), (True, True, True))
        self.assertEqual((part['cast'], part['with_photo'], part['ru_names']), (3, 2, 1))
        text = feed.summarize_names([part, None])
        self.assertIn('1 titles', text)
        self.assertIn('cast photos 66%', text)
        self.assertEqual(feed.summarize_names([None]), 'no titles')


if __name__ == '__main__':
    unittest.main()
