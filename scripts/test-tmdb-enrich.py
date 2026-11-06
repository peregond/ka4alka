import datetime
import unittest

import tmdb_enrich as tmdb
from kinopoisk_ratings import fill_missing_texts

NOW = datetime.datetime(2026, 11, 6, 9, 0, tzinfo=datetime.timezone.utc)


def row(slug, **extra):
    return dict(id='movies:' + slug, section='movies', title='Тест', year=2026, pageUrl='https://w6.zona.plus/movies/' + slug, **extra)


class TmdbTests(unittest.TestCase):
    def test_find_queue_takes_titles_with_imdb_id_newest_first_and_waits_a_month_after_a_miss(self):
        rows = [row('a', imdbId='tt1000001'), dict(row('old', imdbId='tt1000002'), year=2015), row('none'),
                row('known', imdbId='tt1000003', tmdbId=5), row('missed', imdbId='tt1000004', tmdbCheckedAtUtc='2026-10-20T00:00:00Z'),
                row('retry', imdbId='tt1000005', tmdbCheckedAtUtc='2026-09-01T00:00:00Z'), row('bad', imdbId='x1')]
        self.assertEqual(tmdb.find_rows(rows, 10, NOW), ['movies:a', 'movies:retry', 'movies:old'])
        self.assertEqual(tmdb.find_rows(rows, 1, NOW), ['movies:a'])

    def test_card_keeps_the_cover_and_texts_only_for_gaps(self):
        card = dict(poster_path='/abc12.jpg', overview='  Описание  ', genres=[dict(name='Драма'), {}], production_countries=[dict(name='Россия')])
        full = row('a', description='Своё', genre='комедия', country='США')
        self.assertEqual(tmdb.card_fields(full, card), {'tmdbPoster': '/abc12.jpg'})
        empty = row('b')
        self.assertEqual(tmdb.card_fields(empty, card), {'tmdbPoster': '/abc12.jpg', 'tmdbOverview': 'Описание', 'tmdbGenre': 'драма', 'tmdbCountry': 'Россия'})
        # Kinopoisk texts already fill the gap, and a cover path that is not a plain path is not kept.
        covered = row('c', kinopoiskDescription='Кинопоиск', kinopoiskGenre='драма')
        self.assertEqual(tmdb.card_fields(covered, dict(card, poster_path='//evil.example/x.jpg')), {'tmdbCountry': 'Россия'})

    def test_enrich_finds_ids_reads_cards_once_and_marks_attempts(self):
        calls = []
        def get(path, token, **params):
            calls.append(path)
            if path.startswith('/find/'):
                return {'movie_results': [{'id': 7}]} if path.endswith('tt1000001') else {'movie_results': []}
            return {'overview': 'Текст', 'poster_path': '/zzzzz1.jpg', 'genres': [], 'production_countries': []}
        rows = [row('a', imdbId='tt1000001'), row('b', imdbId='tt1000002')]
        result = tmdb.enrich(rows, 'k', NOW, get, budget_seconds=30)
        a, b = result
        self.assertEqual((a['tmdbId'], a['tmdbOverview'], a['tmdbPoster']), (7, 'Текст', '/zzzzz1.jpg'))
        self.assertIn('tmdbTextAtUtc', a)
        self.assertNotIn('tmdbId', b)
        self.assertIn('tmdbCheckedAtUtc', b)
        self.assertEqual(sorted(calls), ['/find/tt1000001', '/find/tt1000002', '/movie/7'])
        again = []
        tmdb.enrich(result, 'k', NOW, lambda path, token, **p: again.append(path) or {}, budget_seconds=30)
        self.assertEqual(again, [])  # neither the card nor the miss is asked again

    def test_failures_stop_and_no_key_means_no_requests(self):
        rows = [row('a', imdbId='tt1000001'), row('b', imdbId='tt1000002')]
        def refused(path, token, **params):
            raise tmdb.Stop(401)
        out = tmdb.enrich(rows, 'k', NOW, refused, budget_seconds=30)
        self.assertEqual(out, rows)
        def broken(path, token, **params):
            raise OSError('down')
        out = tmdb.enrich(rows, 'k', NOW, broken, budget_seconds=30)
        self.assertNotIn('tmdbCheckedAtUtc', out[0])  # a failed attempt is retried
        self.assertEqual(tmdb.enrich(rows, '', NOW, lambda *a, **k: self.fail('no key, no request')), rows)

    def test_gaps_are_filled_from_zona_then_kinopoisk_then_tmdb(self):
        rows = [row('a', kinopoiskDescription='КП', tmdbOverview='TMDB', tmdbGenre='драма', tmdbCountry='США'),
                row('b', tmdbOverview='TMDB', tmdbGenre='драма'), row('c', description='Свой', tmdbOverview='TMDB')]
        filled = fill_missing_texts(rows)
        self.assertEqual((filled[0]['description'], filled[0]['genre'], filled[0]['country']), ('КП', 'драма', 'США'))
        self.assertEqual((filled[1]['description'], filled[1]['genre']), ('TMDB', 'драма'))
        self.assertEqual(filled[2]['description'], 'Свой')


if __name__ == '__main__':
    unittest.main()
