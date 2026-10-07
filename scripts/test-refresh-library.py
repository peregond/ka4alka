import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('refresh_library', Path(__file__).with_name('refresh-library.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CollectorTests(unittest.TestCase):
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


if __name__ == '__main__':
    unittest.main()
