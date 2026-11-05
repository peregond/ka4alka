import datetime
import unittest
import urllib.error

from kinopoisk_ratings import QuotaReached, lookup, refresh, score, select_rows
from library_metadata import merge_metadata

NOW = datetime.datetime(2026, 11, 5, 5, 0, tzinfo=datetime.timezone.utc)


def row(slug, **extra):
    return dict(id='movies:' + slug, section='movies', title='Тест', year=2026, pageUrl='https://w6.zona.plus/movies/' + slug, **extra)


class KinopoiskTests(unittest.TestCase):
    def test_score_range(self):
        self.assertEqual(score(7.94), '7.9')
        self.assertEqual(score('8'), '8.0')
        for bad in (0, None, 'x', 11, -1):
            self.assertIsNone(score(bad))

    def test_identity_comes_only_from_imdb_id_and_must_be_unique(self):
        answer = {'items': [{'kinopoiskId': 5, 'imdbId': 'tt1234567', 'ratingKinopoisk': 7.3}]}
        self.assertEqual(lookup(row('a', imdbId='tt1234567'), 't', lambda url, token: answer), (5, '7.3'))
        two = {'items': answer['items'] + [{'kinopoiskId': 6, 'imdbId': 'tt1234567', 'ratingKinopoisk': 5}]}
        self.assertEqual(lookup(row('a', imdbId='tt1234567'), 't', lambda url, token: two), (None, None))
        other = {'items': [{'kinopoiskId': 5, 'imdbId': 'tt7654321', 'ratingKinopoisk': 7.3}]}
        self.assertEqual(lookup(row('a', imdbId='tt1234567'), 't', lambda url, token: other), (None, None))
        self.assertEqual(lookup(row('a'), 't', lambda url, token: self.fail('no identity, no request')), (None, None))

    def test_known_id_is_refreshed_by_id_and_rating_may_be_missing(self):
        urls = []
        def get(url, token):
            urls.append(url)
            return {'ratingKinopoisk': None}
        self.assertEqual(lookup(row('a', kinopoiskId=9), 't', get), (9, None))
        self.assertTrue(urls[0].endswith('/films/9'))

    def test_unscored_titles_go_first_and_fresh_ones_wait(self):
        rows = [row('scored', imdbId='tt1000001', kinopoisk='7.0', kinopoiskCheckedAtUtc='2026-10-01T00:00:00Z'),
                row('new', imdbId='tt1000002'),
                row('fresh', imdbId='tt1000003', kinopoiskCheckedAtUtc='2026-11-04T00:00:00Z'),
                row('noid')]
        self.assertEqual(select_rows(rows, 10, NOW), ['movies:new', 'movies:scored'])
        self.assertEqual(select_rows(rows, 1, NOW), ['movies:new'])

    def test_refresh_keeps_old_score_on_failure_and_stops_on_quota(self):
        calls = []
        def get(url, token):
            calls.append(url)
            if 'tt1000002' in url:
                raise urllib.error.URLError('down')
            if 'tt1000003' in url:
                raise QuotaReached(402)
            return {'items': [{'kinopoiskId': 1, 'imdbId': 'tt1000001', 'ratingKinopoisk': 8.1}]}
        rows = [row('a', imdbId='tt1000001'), row('b', imdbId='tt1000002', kinopoisk='6.0'), row('c', imdbId='tt1000003'), row('d', imdbId='tt1000004')]
        result, done = refresh(rows, 'token', 10, NOW, get, pause=0)
        self.assertEqual(done, 2)
        self.assertEqual(len(calls), 3)
        self.assertEqual((result[0]['kinopoisk'], result[0]['kinopoiskId']), ('8.1', 1))
        self.assertEqual(result[1]['kinopoisk'], '6.0')
        self.assertIn('kinopoiskCheckedAtUtc', result[1])
        self.assertNotIn('kinopoiskCheckedAtUtc', result[2])
        self.assertNotIn('kinopoiskCheckedAtUtc', result[3])

    def test_no_token_means_no_requests(self):
        rows = [row('a', imdbId='tt1000001')]
        result, done = refresh(rows, '', 10, NOW, lambda *a: self.fail('request without token'))
        self.assertEqual((result, done), (rows, 0))

    def test_page_score_does_not_replace_the_official_one(self):
        self.assertEqual(merge_metadata(row('a', kinopoiskId=3, kinopoisk='8.1'), {'kinopoisk': '7.0'})['kinopoisk'], '8.1')
        self.assertEqual(merge_metadata(row('a', kinopoisk='8.1'), {'kinopoisk': '7.0'})['kinopoisk'], '7.0')


if __name__ == '__main__':
    unittest.main()
