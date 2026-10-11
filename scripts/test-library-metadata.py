import gzip
import datetime
import concurrent.futures
import json
import unittest
from unittest.mock import patch
from library_metadata import parse_detail, imdb_identity, official_scores, enrich_one, enrich, select_details, merge_metadata


class MetadataTests(unittest.TestCase):
    now = datetime.datetime(2026, 10, 11, 5, 0, tzinfo=datetime.timezone.utc)

    @staticmethod
    def item(slug, **metadata):
        return dict(id='movies:' + slug, section='movies', title='Тест', year=2026,
                    pageUrl='https://w6.zona.plus/movies/' + slug, **metadata)

    def test_real_alias_and_zero_rating(self):
        data = parse_detail('<meta itemprop="alternativeHeadline" content="L&#039;objet du délit"><span class="entity-rating-kp">0</span><span class="entity-rating-imdb">6.9</span>')
        self.assertEqual(data['originalTitle'], "L'objet du délit")
        self.assertNotIn('kinopoisk', data)
        self.assertEqual(data['imdb'], '6.9')
        self.assertEqual(parse_detail('<meta itemprop="alternativeHeadline" content="L\'objet du délit">')['originalTitle'], "L'objet du délit")

    def test_identity_rejects_remake_and_ambiguous_titles(self):
        item = dict(section='movies', year=2026, originalTitle='The Weight')
        rows = [dict(id='tt10794054', l='The Weight', y=2026, qid='movie'), dict(id='tt2384688', l='The Weight', y=2012, qid='movie')]
        self.assertEqual(imdb_identity(item, {'d': rows}), 'tt10794054')
        self.assertIsNone(imdb_identity(item, {'d': [dict(id='tt9999999', l='Another film', y=2026, qid='movie')]}))
        self.assertIsNone(imdb_identity(item, {'d': rows+[dict(id='tt9999999', l='The Weight', y=2026, qid='movie')]}))
        self.assertEqual(imdb_identity(dict(section='movies',year=2026,originalTitle="L'objet du délit"), {'d':[dict(id='tt37535559', l='Crescendo', y=2026, qid='movie')]}), 'tt37535559')

    def test_official_votes_required_and_unrelated_ids_omitted(self):
        data = gzip.compress(b'tconst\taverageRating\tnumVotes\ntt10794054\t6.9\t3425\ntt9999999\t8.8\t500\ntt37535559\t0.0\t0\n')
        self.assertEqual(official_scores(data, {'tt10794054','tt37535559'}), {'tt10794054':'6.9'})

    def test_failed_detail_attempt_preserves_rich_metadata_and_rotates_queue(self):
        prior = self.item('known', description='Сохранённый сюжет', imdb='7.5', imdbId='tt1234567',
                          people=[dict(name='Актёр', role='Актёры', pageUrl='', sourcePersonId='42')],
                          metadataUpdatedAtUtc='2026-10-01T05:00:00Z')
        with patch('library_metadata.read', side_effect=OSError('offline')):
            fresh = enrich_one(prior, self.now)
        self.assertEqual(fresh['description'], prior['description'])
        self.assertEqual(fresh['people'], prior['people'])
        self.assertEqual(fresh['imdb'], '7.5')
        self.assertEqual(fresh['metadataUpdatedAtUtc'], prior['metadataUpdatedAtUtc'])
        selected = select_details([fresh, self.item('untouched')], 1, self.now)
        self.assertEqual(selected[0]['id'], 'movies:untouched')

    def test_stale_rotation_moves_beyond_the_first_hundred_records(self):
        rows = [self.item('movie-' + str(index)) for index in range(130)]
        first = select_details(rows, 120, self.now)
        checked_ids = {row['id'] for row in first}
        rows = [{**row, 'metadataCheckedAtUtc': self.now.isoformat()} if row['id'] in checked_ids else row for row in rows]
        next_day = select_details(rows, 120, self.now + datetime.timedelta(days=1))
        self.assertEqual(len(next_day), 10)
        self.assertFalse(checked_ids.intersection(row['id'] for row in next_day))

    def test_official_scores_refresh_all_mapped_records_even_without_detail_checks(self):
        rows = [self.item('a', imdbId='tt1234567', imdb='6.0'), self.item('b', imdbId='tt7654321', imdb='5.0')]
        scores = gzip.compress(b'tconst\taverageRating\tnumVotes\ntt1234567\t7.1\t100\ntt7654321\t8.2\t200\n')
        with patch('library_metadata.read', return_value=scores) as request:
            fresh = enrich(rows, limit=0, now=self.now)
        self.assertEqual([row['imdb'] for row in fresh], ['7.1', '8.2'])
        self.assertEqual(request.call_count, 1)

    def test_other_provider_score_is_never_labeled_as_imdb(self):
        item = self.item('a', imdbId='tt1234567')
        data = json.dumps({'movie': {'name_rus': 'Тест', 'year': 2026, 'description': 'Настоящий сюжет', 'vote_average': 9.9}}).encode()
        with patch('library_metadata.read', return_value=data):
            fresh = enrich_one(item, self.now)
        self.assertEqual(fresh['description'], 'Настоящий сюжет')
        self.assertNotIn('imdb', fresh)

    def test_time_budget_drains_only_four_started_lookups_and_keeps_unstarted_rows(self):
        rows = [self.item('movie-' + str(index), description='Сохранённый сюжет') for index in range(10)]
        clock, submitted = [0], {}
        now = self.now
        class ControlledExecutor:
            def __init__(self, max_workers):
                self.max_workers = max_workers
            def __enter__(self):
                return self
            def __exit__(self, *args):
                return False
            def submit(self, function, *args):
                future = concurrent.futures.Future()
                submitted[future] = (function, args)
                return future
        def complete_one(pending, return_when):
            self.assertLessEqual(len(pending), 4)
            clock[0] = 10  # The deadline passes while the first four requests run.
            future = next(iter(pending))
            function, args = submitted[future]
            try:
                future.set_result(function(*args))
            except Exception as error:
                future.set_exception(error)
            return {future}, set(pending) - {future}
        def lookup(row, stamp):
            if row['id'] == 'movies:movie-0':
                raise OSError('offline')
            return {**row, 'metadataCheckedAtUtc': stamp.isoformat().replace('+00:00', 'Z')}
        with patch('library_metadata.time.monotonic', side_effect=lambda: clock[0]), \
             patch('library_metadata.concurrent.futures.ThreadPoolExecutor', ControlledExecutor), \
             patch('library_metadata.concurrent.futures.wait', side_effect=complete_one), \
             patch('library_metadata.enrich_one', side_effect=lookup):
            fresh = enrich(rows, limit=10, now=now, budget_seconds=5)
        self.assertEqual(len(submitted), 4)
        self.assertEqual(sum('metadataCheckedAtUtc' in row for row in fresh), 4)
        self.assertEqual(fresh[0]['description'], 'Сохранённый сюжет')
        self.assertEqual(fresh[4:], rows[4:])

    def test_zero_time_budget_does_not_mark_any_unattempted_record(self):
        rows = [self.item('unattempted')]
        with patch('library_metadata.enrich_one') as lookup:
            fresh = enrich(rows, limit=1, now=self.now, budget_seconds=0)
        lookup.assert_not_called()
        self.assertEqual(fresh, rows)

    def test_null_previous_credits_accept_fresh_metadata_and_bad_ids_are_skipped(self):
        credit = dict(name='Актёр', role='Актёры', pageUrl='')
        merged = merge_metadata(self.item('known', people=None), {'people': [credit], 'description': 'Новый сюжет'})
        self.assertEqual(merged['people'], [credit])
        self.assertEqual(merged['description'], 'Новый сюжет')
        rows = [self.item('movie-' + str(index), imdbId=value) for index, value in enumerate([None, False, 0, [], {}])]
        with patch('library_metadata.read') as request:
            self.assertEqual(enrich(rows, limit=0, now=self.now), rows)
        request.assert_not_called()


if __name__ == '__main__':
    unittest.main()
