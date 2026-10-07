import gzip
import unittest
from library_metadata import parse_detail, imdb_identity, official_scores


class MetadataTests(unittest.TestCase):
    def test_real_alias_and_zero_rating(self):
        data = parse_detail('<meta itemprop="alternativeHeadline" content="L&#039;objet du délit"><span class="entity-rating-kp">0</span><span class="entity-rating-imdb">6.9</span>')
        self.assertEqual(data['originalTitle'], "L'objet du délit")
        self.assertNotIn('kinopoisk', data)
        self.assertEqual(data['imdb'], '6.9')

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


if __name__ == '__main__':
    unittest.main()
