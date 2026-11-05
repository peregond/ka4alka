import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location('probe', pathlib.Path(__file__).with_name('probe-tmdb-coverage.py'))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


def row(**extra):
    return dict(id='movies:a', section='movies', title='Тест', year=2026, **extra)


class ProbeTests(unittest.TestCase):
    def test_title_with_imdb_id_is_found_through_it_and_fields_are_read(self):
        calls = []
        def fetch(path, token, **params):
            calls.append((path, params))
            if path.startswith('/find/'):
                return {'movie_results': [{'id': 5}]}
            return {'overview': 'Описание', 'poster_path': '/p.jpg', 'genres': [{'id': 1}], 'production_countries': [],
                    'origin_country': ['US'], 'credits': {'cast': [1, 2, 3], 'crew': [{'job': 'Director'}]},
                    'external_ids': {'imdb_id': 'tt1'}, 'vote_average': 7.1}
        card = probe.describe(row(imdbId='tt0000001'), 'k', fetch)
        self.assertEqual(calls[0][0], '/find/tt0000001')
        self.assertTrue(all(card[name] for name in probe.FIELDS))
        self.assertEqual(card['how'], 'imdb')

    def test_ambiguous_or_unknown_titles_are_not_guessed(self):
        self.assertIsNone(probe.describe(row(imdbId='tt0000001'), 'k', lambda *a, **k: {'movie_results': []}))
        self.assertIsNone(probe.describe(row(), 'k', lambda *a, **k: {'results': [{'id': 1}, {'id': 2}]}))
        self.assertIsNone(probe.describe(dict(row(), year=0), 'k', lambda *a, **k: self.fail('nothing to search by')))

    def test_series_use_the_tv_endpoints_and_need_no_director(self):
        seen = []
        def fetch(path, token, **params):
            seen.append(path)
            return {'results': [{'id': 9}]} if path.startswith('/search/') else {'overview': '', 'credits': {}}
        card = probe.describe(dict(row(), section='series'), 'k', fetch)
        self.assertEqual(seen, ['/search/tv', '/tv/9'])
        self.assertTrue(card['directors'])
        self.assertFalse(card['overview'])

    def test_summary_reports_coverage_by_group(self):
        known = dict(how='imdb', **{name: True for name in probe.FIELDS})
        text = probe.summarize([(row(imdbId='tt1'), known), (row(imdbId='tt2'), None), (row(), None)])
        self.assertIn('all: 3 sampled, found 1 (33%)', text)
        self.assertIn('with IMDb id: 2 sampled, found 1 (50%)', text)
        self.assertIn('without IMDb id: 1 sampled, found 0 (0%)', text)


if __name__ == '__main__':
    unittest.main()
