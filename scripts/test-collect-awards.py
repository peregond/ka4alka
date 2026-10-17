#!/usr/bin/env python3
"""Offline checks for collect_awards.py with a hand-made Wikidata answer: nothing here reaches the network."""
import copy, os, sys, tempfile, json
sys.path.insert(0, os.path.dirname(__file__))
import collect_awards as awards


def row(imdb, date, en, ru='', original='', published='', poster=''):
    cell = lambda text: {'type': 'literal', 'value': text}
    result = {'imdb': cell(imdb), 'date': cell(date), 'labelEn': cell(en)}
    for key, text in (('labelRu', ru), ('original', original), ('published', published), ('poster', poster)):
        if text:
            result[key] = cell(text)
    return result


def response(*rows):
    return {'results': {'bindings': list(rows)}}


def small(anchors, minimum=1, maximum=10, lag=(0, 2)):
    award = copy.deepcopy(awards.AWARDS[0])
    award.update(anchors=anchors, minimum=minimum, maximum=maximum, lag=lag)
    return award


def expect_error(action, text):
    try:
        action()
    except ValueError as error:
        assert text in str(error), f'expected "{text}" in "{error}"'
        return
    raise AssertionError(f'expected an error with "{text}"')


rows = [
    row('tt11271038', '2025-03-02T00:00:00Z', 'Anora', 'Анора', 'Anora', '2024-05-21T00:00:00Z',
        'http://commons.wikimedia.org/wiki/Special:FilePath/Anora%20poster.jpg'),
    row('tt11271038', '2025-03-02T00:00:00Z', 'Anora', '', '', '2024-10-18T00:00:00Z'),  # a second row of the same film
    row('tt15398776', '2024-03-10T00:00:00Z', 'Oppenheimer', 'Оппенгеймер', '', '2023-07-11T00:00:00Z'),
    row('nm0000001', '2020-01-01T00:00:00Z', 'Not a film'),  # a person id is never a title
    row('tt0000001', '', 'No year'),  # no award year, nothing to list
]
entries = awards.entries_from(response(*rows), awards.AWARDS[0])
assert [e['imdbId'] for e in entries] == ['tt11271038', 'tt15398776'], entries
anora = entries[0]
assert anora['year'] == 2025 and anora['filmYear'] == 2024, 'the earliest publication is the film year'
assert anora['title'] == 'Анора' and anora['titleEn'] == 'Anora' and anora['originalTitle'] == 'Anora'
assert anora['poster'] == 'https://commons.wikimedia.org/wiki/Special:FilePath/Anora%20poster.jpg?width=300', anora['poster']
assert 'poster' not in entries[1], 'an entry without a poster has no poster field'
assert entries[1]['originalTitle'] == 'Oppenheimer', 'the English title stands in for a missing original title'
assert awards.poster_url('https://example.org/x.jpg') is None, 'only Commons images are kept'

award = small({2025: 'anora', 2024: 'oppenheimer'}, minimum=2, maximum=2)
awards.check(award, entries)
expect_error(lambda: awards.check(small({2025: 'titanic'}), entries), 'should be')
expect_error(lambda: awards.check(small({}, minimum=3), entries), 'entries, expected')
bad = copy.deepcopy(entries); bad[0]['filmYear'] = 2019
expect_error(lambda: awards.check(small({}), bad), 'is a 2019 film')

# The anchor winners need a full list, so the whole document is built from a larger answer.
full = [row(f'tt{1000000 + i}', f'{1990 + i}-03-01T00:00:00Z', f'Film {i}', published=f'{1989 + i}-01-01T00:00:00Z') for i in range(36)]
full += [row('tt9000001', '2025-03-02T00:00:00Z', 'Anora', published='2024-01-01T00:00:00Z'),
         row('tt9000002', '2024-03-10T00:00:00Z', 'Oppenheimer', published='2023-01-01T00:00:00Z'),
         row('tt9000003', '2020-02-09T00:00:00Z', 'Parasite', published='2019-01-01T00:00:00Z'),
         row('tt9000004', '1998-03-23T00:00:00Z', 'Titanic', published='1997-01-01T00:00:00Z'),
         row('tt9000005', '1995-03-27T00:00:00Z', 'Forrest Gump', published='1994-01-01T00:00:00Z')]
relaxed = copy.deepcopy(awards.AWARDS[0]); relaxed['minimum'] = 40
original = awards.AWARDS[:]
try:
    awards.AWARDS[:] = [relaxed]
    document = awards.build({'oscar-best-picture': response(*full)})
finally:
    awards.AWARDS[:] = original
assert document['schemaVersion'] == 1 and document['source'] == 'Wikidata' and document['generatedAtUtc'].endswith('Z')
listed = document['awards'][0]['entries']
assert len(listed) == 41 and listed[0]['year'] == 2025, 'newest first'
assert all(listed[i]['year'] >= listed[i + 1]['year'] for i in range(len(listed) - 1))
expect_error(lambda: awards.build({}), 'no awards collected')

with tempfile.TemporaryDirectory() as folder:
    source = os.path.join(folder, 'response.json'); target = os.path.join(folder, 'awards.json')
    with open(source, 'w', encoding='utf-8') as handle:
        json.dump(response(*full), handle)
    try:
        awards.AWARDS[:] = [relaxed]
        assert awards.main(['--from-file', source, '--award', 'oscar-best-picture', '--output', target]) == 0
    finally:
        awards.AWARDS[:] = original
    written = json.load(open(target, encoding='utf-8'))
    assert written['awards'][0]['id'] == 'oscar-best-picture' and len(written['awards'][0]['entries']) == 41
print('PASS: Wikidata rows become entries, checks reject wrong lists, the file is written for the app')
