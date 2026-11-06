import datetime
import unittest
import urllib.error

from kinopoisk_ratings import QuotaReached, details, people_rows, refresh_people, staff_people, fill_missing_texts, film_text, lookup, needs_text, refresh, score, search, select_rows
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
        self.assertEqual(lookup(dict(row('a'), year=0), 't', lambda url, token: self.fail('nothing to search by, no request')), (None, None))

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
        self.assertEqual(select_rows(rows, 10, NOW), ['movies:new', 'movies:noid', 'movies:scored'])  # a title without any id is searched by name
        self.assertEqual(select_rows(rows, 1, NOW), ['movies:new'])

    def test_old_scored_films_are_frozen_and_old_misses_retried_monthly(self):
        old = dict(year=2019)
        rows = [dict(row('frozen', imdbId='tt1000001', kinopoisk='8.0', kinopoiskId=1, kinopoiskTextAtUtc='2026-10-01T00:00:00Z'), **old),
                dict(row('lastyear', imdbId='tt1000002', kinopoisk='7.0', kinopoiskId=2, kinopoiskCheckedAtUtc='2026-10-01T00:00:00Z'), year=2025),
                dict(row('miss-recent', imdbId='tt1000003', kinopoiskCheckedAtUtc='2026-10-20T00:00:00Z'), **old),
                dict(row('miss-old', imdbId='tt1000004', kinopoiskCheckedAtUtc='2026-09-20T00:00:00Z'), **old),
                dict(row('never', imdbId='tt1000005'), **old),
                dict(row('no-id-score', imdbId='tt1000006', kinopoisk='6.0'), **old)]
        # A page score without a Kinopoisk id is not final: it is confirmed once through the API.
        chosen = select_rows(rows, 10, NOW, text_share=0)
        self.assertNotIn('movies:frozen', chosen)
        self.assertNotIn('movies:miss-recent', chosen)
        self.assertEqual(set(chosen), {'movies:never', 'movies:miss-old', 'movies:lastyear', 'movies:no-id-score'})
        self.assertEqual(chosen[:2], ['movies:never', 'movies:miss-old'])

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

    def test_title_without_identity_is_found_by_name_year_and_kind(self):
        def item(kp, year=2026, kind='FILM', **names):
            return dict(kinopoiskId=kp, year=year, type=kind, ratingKinopoisk=7.4, **names)
        film = dict(row('x'), title='Тест', originalTitle='The Test')
        urls = []
        def get(items):
            def fetch(url, token):
                urls.append(url)
                return {'items': items}
            return fetch
        self.assertEqual(search(film, 't', get([item(1, nameRu='Тест')])), (1, '7.4'))
        self.assertIn('keyword=%D0%A2%D0%B5%D1%81%D1%82', urls[0])
        self.assertIn('yearFrom=2026', urls[0])
        # An English name of the entry is enough when it matches the original title.
        self.assertEqual(search(film, 't', get([item(2, nameRu='Другой', nameOriginal='The Test')])), (2, '7.4'))
        # Wrong year, wrong kind, other name or more than one candidate: nothing is guessed.
        self.assertEqual(search(film, 't', get([item(3, year=2025, nameRu='Тест')])), (None, None))
        self.assertEqual(search(film, 't', get([item(4, kind='TV_SERIES', nameRu='Тест')])), (None, None))
        self.assertEqual(search(film, 't', get([item(5, nameRu='Тестовый')])), (None, None))
        self.assertEqual(search(film, 't', get([item(6, nameRu='Тест'), item(7, nameRu='Тест')])), (None, None))
        series = dict(row('s'), section='series', title='Тест')
        self.assertEqual(search(series, 't', get([item(8, kind='MINI_SERIES', nameRu='Тест')])), (8, '7.4'))
        self.assertEqual(search(dict(film, year=0), 't', lambda *a: self.fail('no year, no request')), (None, None))
        # The id is kept even when Kinopoisk has no score yet.
        unrated = dict(item(9, nameRu='Тест'), ratingKinopoisk=None)
        self.assertEqual(search(film, 't', get([unrated])), (9, None))

    def test_film_card_gives_russian_texts_and_skips_a_description_already_stored(self):
        data = dict(nameRu='  Тест ', description='Описание\xa0фильма  ', genres=[dict(genre='драма'), dict(genre='комедия'), {}],
                    countries=[dict(country='Россия')], ratingKinopoisk=7.26)
        self.assertEqual(film_text(data), dict(kinopoiskTitle='Тест', kinopoiskDescription='Описание фильма',
                                               kinopoiskGenre='драма, комедия', kinopoiskCountry='Россия'))
        self.assertNotIn('kinopoiskDescription', film_text(data, 'Описание фильма'))
        self.assertEqual(film_text(dict(nameRu=5, description='', genres=None)), {})
        self.assertEqual(details(dict(kinopoiskId=4), 't', lambda url, token: data)[:2], (4, '7.3'))

    def test_card_never_read_for_texts_is_read_whatever_the_cooldown(self):
        recent = row('a', imdbId='tt1000001', kinopoiskId=3, kinopoisk='7.0', kinopoiskCheckedAtUtc='2026-11-04T00:00:00Z')
        self.assertTrue(needs_text(recent, NOW))
        self.assertEqual(select_rows([recent], 5, NOW), ['movies:a'])
        done = dict(recent, kinopoiskTextAtUtc='2026-11-04T00:00:00Z')
        self.assertFalse(needs_text(done, NOW))
        self.assertEqual(select_rows([done], 5, NOW), [])
        old = dict(done, year=2015)
        # An old film that has its own description is not read for texts; one without it is.
        with_text = dict(old, kinopoiskTextAtUtc=None, description='Своё описание')
        self.assertFalse(needs_text(with_text, NOW))
        self.assertEqual(select_rows([with_text], 5, NOW), [])
        without = dict(old, kinopoiskTextAtUtc=None)
        self.assertTrue(needs_text(without, NOW))
        self.assertEqual(select_rows([without], 5, NOW), ['movies:a'])
        self.assertEqual(select_rows([old], 5, NOW), [])

    def test_a_share_of_every_run_is_kept_for_cards_never_read_for_texts(self):
        unscored = [row(f'u{i}', imdbId=f'tt20000{i:02d}') for i in range(10)]
        texts = [row(f't{i}', imdbId=f'tt30000{i:02d}', kinopoiskId=100 + i, kinopoisk='7.0', kinopoiskCheckedAtUtc='2026-11-04T00:00:00Z') for i in range(5)]
        chosen = select_rows(unscored + texts, 8, NOW, text_share=3)
        self.assertEqual(len(chosen), 8)
        self.assertEqual(sum(1 for item in chosen if item.startswith('movies:t')), 3)
        # With no cards waiting, the whole run goes to the others; with a share of zero, the old order holds.
        self.assertEqual(len(select_rows(unscored, 8, NOW, text_share=3)), 8)
        self.assertEqual(sum(1 for item in select_rows(unscored + texts, 8, NOW, text_share=0) if item.startswith('movies:t')), 0)
        self.assertEqual(len(select_rows(unscored + texts, 2, NOW, text_share=5)), 2)

    def test_refresh_stores_texts_once_and_failure_does_not_mark_them_read(self):
        card = dict(nameRu='Тест', description='Описание', genres=[dict(genre='драма')], countries=[dict(country='США')], ratingKinopoisk=6.5)
        rows = [row('a', kinopoiskId=1), row('b', kinopoiskId=2, description='Описание'), row('c', kinopoiskId=3)]  # all this year's films
        def get(url, token):
            if url.endswith('/3'):
                raise urllib.error.URLError('down')
            return card
        result, done = refresh(rows, 't', 10, NOW, get, pause=0)
        self.assertEqual(done, 3)
        a, b, c = result
        self.assertEqual((a['kinopoiskDescription'], a['kinopoiskTitle'], a['kinopoisk']), ('Описание', 'Тест', '6.5'))
        self.assertIn('kinopoiskTextAtUtc', a)
        self.assertNotIn('kinopoiskDescription', b)
        self.assertIn('kinopoiskTextAtUtc', b)
        self.assertNotIn('kinopoiskTextAtUtc', c)

    def test_a_title_lacking_texts_shows_the_kinopoisk_ones(self):
        rows = [dict(row('a'), kinopoiskDescription='Из Кинопоиска', kinopoiskGenre='драма', kinopoiskCountry='США'),
                dict(row('b', description='Своё', genre='комедия', country='Россия'), kinopoiskDescription='Из Кинопоиска',
                     kinopoiskGenre='драма', kinopoiskCountry='США'),
                dict(row('c'), description='  ', genre='', kinopoiskDescription='Из Кинопоиска', kinopoiskGenre='триллер'),
                row('d')]
        filled = fill_missing_texts(rows)
        self.assertEqual([(x.get('description'), x.get('genre'), x.get('country')) for x in filled],
                         [('Из Кинопоиска', 'драма', 'США'), ('Своё', 'комедия', 'Россия'), ('Из Кинопоиска', 'триллер', None), (None, None, None)])
        self.assertIs(filled[1], rows[1])

    def test_cast_is_taken_in_russian_with_limits_per_role(self):
        crew = [dict(professionKey='ACTOR', nameRu=f'Актёр {i}', nameEn=f'Actor {i}') for i in range(15)]
        crew += [dict(professionKey='DIRECTOR', nameRu='Режиссёр', nameEn=''), dict(professionKey='OPERATOR', nameRu='', nameEn='Camera Man'),
                 dict(professionKey='COMPOSER', nameRu='Композитор'), dict(professionKey='ACTOR', nameRu=' ', nameEn=''), 'junk']
        people = staff_people(crew)
        self.assertEqual(sum(1 for p in people if p['role'] == 'Актёры'), 12)
        self.assertEqual(people[0], {'name': 'Актёр 0', 'role': 'Актёры', 'originalName': 'Actor 0'})
        self.assertIn({'name': 'Режиссёр', 'role': 'Режиссёры'}, people)
        self.assertIn({'name': 'Camera Man', 'role': 'Операторы'}, people)
        self.assertEqual(staff_people({'error': 1}), [])

    def test_cast_is_read_once_for_recent_films_with_an_id_and_newest_first(self):
        rows = [dict(row('old', kinopoiskId=1), year=2015), row('noid'), row('b', kinopoiskId=2), dict(row('a', kinopoiskId=3), year=2025),
                row('done', kinopoiskId=4, kinopoiskPeopleAtUtc='2026-11-01T00:00:00Z')]
        self.assertEqual(people_rows(rows, 10, NOW), ['movies:b', 'movies:a'])
        self.assertEqual(people_rows(rows, 1, NOW), ['movies:b'])

    def test_cast_refresh_marks_only_successful_reads_and_stops_on_quota(self):
        seen = []
        def get(url, token):
            seen.append(url)
            if 'filmId=2' in url:
                raise urllib.error.URLError('down')
            if 'filmId=3' in url:
                raise QuotaReached(402)
            return [dict(professionKey='ACTOR', nameRu='Иван')]
        rows = [row('a', kinopoiskId=1), row('b', kinopoiskId=2), row('c', kinopoiskId=3), row('d', kinopoiskId=4)]
        result, done = refresh_people(rows, 't', 10, NOW, get, pause=0)
        self.assertEqual(done, 1)
        self.assertEqual(result[0]['kinopoiskPeople'], [{'name': 'Иван', 'role': 'Актёры'}])
        self.assertIn('kinopoiskPeopleAtUtc', result[0])
        for later in result[1:]:
            self.assertNotIn('kinopoiskPeopleAtUtc', later)
        self.assertEqual(len(seen), 3)
        self.assertEqual(refresh_people(rows, '', 10, NOW, get)[1], 0)

    def test_a_title_with_no_cast_shows_the_kinopoisk_cast(self):
        cast = [{'name': 'Иван', 'role': 'Актёры'}]
        rows = [dict(row('a'), kinopoiskPeople=cast), dict(row('b'), people=[{'name': 'Свой', 'role': 'Актёры'}], kinopoiskPeople=cast),
                dict(row('c'), people=[], kinopoiskPeople=cast)]
        filled = fill_missing_texts(rows)
        self.assertEqual([x['people'] for x in filled], [cast, [{'name': 'Свой', 'role': 'Актёры'}], cast])

    def test_page_score_does_not_replace_the_official_one(self):
        self.assertEqual(merge_metadata(row('a', kinopoiskId=3, kinopoisk='8.1'), {'kinopoisk': '7.0'})['kinopoisk'], '8.1')
        self.assertEqual(merge_metadata(row('a', kinopoisk='8.1'), {'kinopoisk': '7.0'})['kinopoisk'], '7.0')


if __name__ == '__main__':
    unittest.main()
