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
# Posters Wikidata lacks come from the supplied function, once per film; a failed lookup leaves the entry as it is.
asked = []
def fake_poster(imdb):
    asked.append(imdb)
    return None if imdb == 'tt15398776' else f'https://img.example/{imdb}.jpg'
sample = [dict(e) for e in entries] + [dict(entries[0], year=2024)]
awards.add_posters(sample, fake_poster, pause=0)
assert asked == ['tt15398776'], 'a poster Wikidata knows is not asked again, a film is asked once: ' + str(asked)
assert sample[0]['poster'].startswith('https://commons.') and 'poster' not in sample[1]

class Reply:
    def __init__(self, text): self.text = text.encode()
    def __enter__(self): return self
    def __exit__(self, *args): return False
    def read(self, *args): return self.text
page = json.dumps({'d': [{'id': 'tt0110912', 'i': {'imageUrl': 'https://m.media-amazon.com/images/M/abc._V1_.jpg'}}]})
assert awards.imdb_poster('tt0110912', lambda request, timeout: Reply(page)) == 'https://m.media-amazon.com/images/M/abc._V1_QL75_UX400_.jpg'
assert awards.imdb_poster('tt0000002', lambda request, timeout: Reply(page)) is None, 'another title is not taken'
def broken(request, timeout): raise OSError('offline')
assert awards.imdb_poster('tt0110912', broken) is None

# A busy Wikidata is asked again after the pause it names; other errors and a last failure surface.
import io, urllib.error
class Headers(dict):
    def get(self, key, default=None): return super().get(key, default)
def busy(code, retry=None):
    return urllib.error.HTTPError('https://query.wikidata.org', code, 'busy', Headers({'Retry-After': retry} if retry else {}), io.BytesIO(b''))
calls, pauses = [], []
def flaky(request, timeout):
    calls.append(1)
    if len(calls) < 3: raise busy(429, '7')
    return Reply(json.dumps(response(*rows[:1])))
result = awards.fetch('Q102427', flaky, pauses.append)
assert len(calls) == 3 and pauses == [7, 7] and result['results']['bindings'], (calls, pauses)
def forbidden(request, timeout): raise busy(403)
try:
    awards.fetch('Q102427', forbidden, pauses.append)
    raise AssertionError('a 403 is not retried')
except urllib.error.HTTPError as error:
    assert error.code == 403
def always(request, timeout): raise busy(503)
try:
    awards.fetch('Q102427', always, pauses.append, attempts=2)
    raise AssertionError('the last failure surfaces')
except urllib.error.HTTPError as error:
    assert error.code == 503

# Nominees: the winner of a year is not listed again, the P1411 query asks for 'nominated for'.
assert 'p:P1411' in awards.query('Q102427', 'P1411') and 'p:P166' in awards.query('Q102427')
winners_document = {'awards': [{'id': 'oscar-best-picture', 'entries': entries}]}
nominee_rows = [
    row('tt11271038', '2025-03-02T00:00:00Z', 'Anora', 'Анора', 'Anora', '2024-05-21T00:00:00Z'),  # the winner is also "nominated"
    row('tt16311594', '2025-03-02T00:00:00Z', 'Emilia Pérez', '', '', '2024-05-18T00:00:00Z'),
    row('tt11271038', '2024-03-10T00:00:00Z', 'Anora', '', '', '2024-05-21T00:00:00Z'),  # same film, other year: kept
]
nominees = awards.nominees_from(response(*nominee_rows), entries)
assert [(e['imdbId'], e['year']) for e in nominees] == [('tt16311594', 2025), ('tt11271038', 2024)], nominees
document = awards.build_nominees({'oscar-best-picture': response(*nominee_rows)}, winners_document, posters=lambda imdb: 'https://example.org/' + imdb + '.jpg')
assert document['schemaVersion'] == 1 and document['awards'][0]['id'] == 'oscar-best-picture'
assert all(e['poster'].startswith('https://example.org/') for e in document['awards'][0]['entries']), 'nominees get the IMDb posters too'
assert awards.build_nominees({}, winners_document)['awards'] == [], 'an award without nominees is left out'
expect_error(lambda: awards.build_nominees({'oscar-best-picture': response(*nominee_rows)}, winners_document, limit=1), 'at most')

print('PASS: Wikidata rows become entries, checks reject wrong lists, the file is written for the app')
