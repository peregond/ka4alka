"""Regression fixtures for identity, credit provenance and bounded text extraction."""
import copy
import json
import unittest

from catalog_text import MAX_BYTES, catalog_url, merge_people, parse_html_metadata, parse_text_metadata, photo_url


ITEM = {'id': 'movies:primer', 'section': 'movies', 'title': 'Детонатор', 'year': 2004,
        'originalTitle': 'Primer', 'pageUrl': 'https://w6.zona.plus/movies/primer'}
JSON = {
    'movie': {'name_rus': 'Детонатор', 'name_original': 'Primer', 'year': 2004, 'serial': False,
              'description': '<p>Два инженера &amp; их открытие.</p>', 'rating_kinopoisk': '7,4',
              'rating_imdb': 6.9, 'rating_tmdb': 9.8, 'genreId': '2 5', 'country_id': '1 2'},
    'genres': [{'id': 2, 'name': 'Фантастика', 'translit': 'fantastika'},
               {'id': 5, 'name': 'Драма', 'translit': 'drama'},
               {'id': 8, 'name': 'Комедия', 'translit': 'komediia'}],
    'countries': [{'id': 1, 'name': 'США', 'translit': 'ssha'},
                  {'id': 2, 'name': 'Великобритания', 'translit': 'velikobritaniia'}],
    'persons': {'actors': [{'i': 321, 'name': 'Шейн Каррут', 'name_eng': 'Shane Carruth',
                           'cover': 'https://img1.zonapic.com/images/actor/0/321.jpg'}],
                'director': [{'i': '321', 'name': 'Шейн Каррут'}],
                'operator': [{'i': '123', 'name': 'Технический участник'}]},
    'recommendations': [{'name_rus': 'Чужой фильм', 'actors': [{'name': 'Чужой актёр'}]}],
}


def encode(data):
    return json.dumps(data, ensure_ascii=False).encode('utf-8')


class TextMetadataTests(unittest.TestCase):
    def test_identified_json_yields_text_ratings_and_only_selected_choices(self):
        result = parse_text_metadata(encode(JSON), ITEM)
        self.assertEqual(result['description'], 'Два инженера & их открытие.')
        self.assertEqual(result['originalTitle'], 'Primer')
        self.assertEqual((result['kinopoisk'], result['imdb']), ('7.4', '6.9'))
        self.assertEqual(result['genreKeys'], ['fantastika', 'drama'])
        self.assertEqual(result['genre'], 'Фантастика, Драма')
        self.assertEqual(result['countryKeys'], ['ssha', 'velikobritaniia'])
        self.assertTrue(result['countryKeysComplete'])
        self.assertEqual({row['name'] for row in result['people']}, {'Шейн Каррут', 'Технический участник'})
        self.assertEqual(result['people'][0]['sourcePersonId'], '321')
        self.assertEqual(result['people'][0]['originalName'], 'Shane Carruth')
        self.assertEqual(result['people'][0]['photoUrl'], 'https://img1.zonapic.com/images/actor/0/321.jpg')
        self.assertNotIn('poster', result)
        self.assertNotIn('releases', result)

    def test_unrelated_json_is_rejected_before_credits_or_ratings(self):
        for mutation in [('name_rus', 'Другой фильм'), ('year', 2005), ('serial', True)]:
            data = copy.deepcopy(JSON)
            data['movie'][mutation[0]] = mutation[1]
            if mutation[0] == 'name_rus':
                data['movie']['name_original'] = 'Unrelated'
            with self.subTest(mutation=mutation):
                self.assertEqual(parse_text_metadata(encode(data), ITEM), {})
        data = copy.deepcopy(JSON)
        data['serial'] = data.pop('movie')
        self.assertEqual(parse_text_metadata(encode(data), ITEM), {})

    def test_original_title_can_confirm_translated_card(self):
        item = dict(ITEM, title='Название другой локализации')
        self.assertIn('people', parse_text_metadata(encode(JSON), item))
        item.pop('originalTitle')
        self.assertEqual(parse_text_metadata(encode(JSON), item), {})

    def test_series_requires_matching_serial_root_and_section(self):
        data = copy.deepcopy(JSON)
        data['serial'] = data.pop('movie')
        data['serial']['serial'] = 'true'
        item = dict(ITEM, section='series', pageUrl='https://w6.zona.plus/tvseries/primer')
        self.assertIn('description', parse_text_metadata(encode(data), item))
        data['serial']['serial'] = False
        self.assertEqual(parse_text_metadata(encode(data), item), {})

    def test_incomplete_countries_do_not_claim_foreign_status(self):
        for mutation in ['1 2 3', '1 2 invalid']:
            data = copy.deepcopy(JSON)
            data['movie']['country_id'] = mutation
            result = parse_text_metadata(encode(data), ITEM)
            self.assertEqual(result['countryKeys'], ['ssha', 'velikobritaniia'])
            self.assertFalse(result['countryKeysComplete'])
        data = copy.deepcopy(JSON)
        data['countries'][1]['translit'] = 'country-2'
        result = parse_text_metadata(encode(data), ITEM)
        self.assertFalse(result['countryKeysComplete'])

    def test_invalid_scores_and_empty_details_cannot_erase_cached_data(self):
        data = copy.deepcopy(JSON)
        data['movie'].update(description='', name_original='', rating_imdb=11, rating_kinopoisk='NaN')
        data['movie'].pop('country_id')
        data['movie'].pop('genreId')
        data['persons'] = {}
        self.assertEqual(parse_text_metadata(encode(data), ITEM), {})
        data['movie']['rating_tmdb'] = 8.5
        data['movie']['rating'] = 9
        self.assertNotIn('imdb', parse_text_metadata(encode(data), ITEM))

    def test_portrait_must_match_person_and_trusted_path(self):
        data = copy.deepcopy(JSON)
        data['persons']['actors'][0]['cover'] = 'https://img1.zonapic.com/images/actor/0/999.jpg'
        self.assertNotIn('photoUrl', parse_text_metadata(encode(data), ITEM)['people'][0])
        for value in ['https://evil.test/images/actor/0/321.jpg',
                      'https://img1.zonapic.com/images/actor/0/321.jpg?token=secret',
                      'https://img1.zonapic.com/images/actor/0/%33321.jpg',
                      'data:image/png;base64,ABC']:
            self.assertIsNone(photo_url(value, '321'))

    def test_html_description_schema_credits_and_trusted_collections(self):
        body = '''<link rel="canonical" href="/movies/primer">
          <article itemscope itemtype="https://schema.org/Movie">
            <h1 itemprop="name">Детонатор</h1>
            <meta content="Primer" itemprop="alternativeHeadline">
            <div itemprop="description">Два <em>инженера</em> &amp; их открытие.</div>
            <span class="entity-rating-kp">7,4</span><span class="entity-rating-imdb">6.9</span>
            <span itemprop="genre">Фантастика</span>
            <a href="/movies/filter/genre-fantastika">Фантастика</a>
            <a href="/movies/filter/country-ssha">США</a>
            <span itemprop="actor"><span itemprop="name">Шейн Каррут</span></span>
            <div itemprop="director"><a href="/persons/shane-carruth"><span itemprop="name">Шейн Каррут</span></a></div>
            <span itemprop="cinematographer">Оператор фильма</span>
            <div itemprop="isPartOf"><a href="/collections/nauka">Наука</a>
              <a href="https://evil.test/collections/fake">Чужая подборка</a></div>
            <li class="results-item-wrap"><span itemprop="actor">Чужой актёр</span>
              <div itemprop="description">Чужой сюжет</div><span class="entity-rating-imdb">9.9</span></li>
          </article>'''
        result = parse_text_metadata(body.encode(), ITEM)
        self.assertEqual(result['description'], 'Два инженера & их открытие.')
        self.assertEqual(result['originalTitle'], 'Primer')
        self.assertEqual(result['imdb'], '6.9')
        self.assertEqual(result['genreKeys'], ['fantastika'])
        self.assertEqual(result['countryKeys'], ['ssha'])
        self.assertEqual([(row['name'], row['role']) for row in result['people']],
                         [('Шейн Каррут', 'Актёры'), ('Шейн Каррут', 'Режиссёры'), ('Оператор фильма', 'Операторы')])
        self.assertEqual(result['people'][1]['pageUrl'], 'https://w6.zona.plus/persons/shane-carruth')
        self.assertEqual(result['collections'], [{'name': 'Наука', 'kind': 'Подборка',
                                                 'pageUrl': 'https://w6.zona.plus/collections/nauka'}])

    def test_html_labelled_link_credits_and_untrusted_links(self):
        body = '''<dl><dt>В ролях</dt><dd><a href="/people/actor">Актёр</a></dd></dl>
        <span itemprop="actor"><a href="https://evil.test/persons/actor">Ещё актёр</a></span>
        <a href="/persons/other">Посторонний человек</a>
        <nav><span itemprop="actor">Навигация</span><a href="/movies/filter/country-rossiia">Россия</a></nav>'''
        result = parse_html_metadata(body)
        self.assertEqual(result['people'], [{'name': 'Актёр', 'role': 'Актёры', 'pageUrl': 'https://w6.zona.plus/people/actor'},
                                           {'name': 'Ещё актёр', 'role': 'Актёры', 'pageUrl': ''}])
        self.assertNotIn('countryKeys', result)

    def test_wrong_canonical_schema_identity_or_section_rejects_html(self):
        for body in ['<link rel="canonical" href="/movies/wrong"><div itemprop="description">Wrong</div>',
                     '<article itemtype="https://schema.org/Movie"><h1 itemprop="name">Wrong</h1><div itemprop="description">Wrong</div></article>',
                     '<article itemtype="https://schema.org/TVSeries"><div itemprop="description">Wrong</div></article>']:
            with self.subTest(body=body):
                self.assertEqual(parse_text_metadata(body.encode(), ITEM), {})

    def test_trusted_urls_reject_ambiguous_or_remote_locations(self):
        self.assertEqual(catalog_url('/persons/актёр/', '/persons/'), 'https://w6.zona.plus/persons/актёр')
        for value in ['http://w6.zona.plus/persons/person', 'https://user@w6.zona.plus/persons/person',
                      'https://w6.zona.plus:444/persons/person', '/persons/person?x=1',
                      '/persons/person#bio', '//evil.test/persons/person', '/persons/../person',
                      '/persons/person_name', '/persons/%70erson']:
            with self.subTest(url=value):
                self.assertIsNone(catalog_url(value, '/persons/'))

    def test_merge_preserves_rich_credit_and_keeps_namesakes_separate(self):
        prior = [{'name': 'Шейн Каррут', 'role': 'Актёры', 'pageUrl': 'https://w6.zona.plus/persons/shane',
                  'originalName': 'Shane Carruth', 'sourcePersonId': '321',
                  'photoUrl': 'https://img1.zonapic.com/images/actor/0/321.jpg'}]
        fresh = [{'name': 'Шейн Каррут', 'role': 'Актёры', 'pageUrl': ''}]
        merged = merge_people(prior, fresh)
        self.assertEqual(merged, prior)
        fresh[0]['sourcePersonId'] = '999'
        self.assertEqual(len(merge_people(prior, fresh)), 2)

    def test_sparse_refresh_preserves_namesakes_identified_by_distinct_profiles(self):
        prior = [{'name': 'Александр Иванов', 'role': 'Актёры', 'pageUrl': '',
                  'profileUrl': 'https://kino-teatr.ua/ru/person/aleksandr-ivanov-101.phtml',
                  'originalName': 'Alexander Ivanov'},
                 {'name': 'Александр Иванов', 'role': 'Актёры', 'pageUrl': '',
                  'profileUrl': 'https://kino-teatr.ua/ru/person/aleksandr-ivanov-202.phtml',
                  'originalName': 'Aleksandr Ivanov'}]
        fresh = [{'name': 'Александр Иванов', 'role': 'Актёры', 'pageUrl': ''}]
        merged = merge_people(prior, fresh)
        self.assertEqual(len(merged), 2)
        self.assertEqual({row['profileUrl'] for row in merged}, {row['profileUrl'] for row in prior})
        self.assertEqual({(row['profileUrl'], row['originalName']) for row in merged},
                         {(row['profileUrl'], row['originalName']) for row in prior})

    def test_malformed_and_oversized_inputs_are_bounded(self):
        for data in [b'', b'{bad json', b'[1,2]', b'\xff', b'no details', b'x' * (MAX_BYTES + 1),
                     ('<div>' * 110 + '<span itemprop="actor">Actor</span>' + '</div>' * 110).encode()]:
            with self.subTest(size=len(data)):
                self.assertEqual(parse_text_metadata(data, ITEM), {})
        self.assertEqual(parse_text_metadata(encode(JSON), dict(ITEM, pageUrl='https://evil.test/movies/primer')), {})


if __name__ == '__main__':
    unittest.main()
