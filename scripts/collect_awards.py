#!/usr/bin/env python3
"""Collects award winners of every year from Wikidata into awards.json for the Windows app.

The app reads this one small file from the catalog-data release (our own backend) and never asks Wikidata itself.
Each entry carries the film's IMDb id: the app shows it as an IMDb-only card, loads its details from the site's
/api/title and searches its releases the usual way.

  python scripts/collect_awards.py --output awards.json
  python scripts/collect_awards.py --from-file wikidata-response.json --award oscar-best-picture --output awards.json

Nominees go to a second file, awards-nominees.json, so that the first stays small for the apps that read only that one.

The file is checked before it is written: shape, uniqueness, plausible counts and a few well known anchor winners,
so a changed or broken Wikidata answer fails the run instead of publishing a wrong list.
"""
import argparse, datetime, json, re, sys, time, urllib.error, urllib.parse, urllib.request

ENDPOINT = 'https://query.wikidata.org/sparql'
USER_AGENT = 'Kachalka-awards/1.0 (https://github.com/peregond/ka4alka)'
FILM_KINDS = ('Q11424',)  # film; subclasses (feature, animated, silent film) are reached through P279

AWARDS = [
    {
        'id': 'oscar-best-picture', 'qid': 'Q102427',
        'title': 'Оскар', 'titleEn': 'Academy Awards',
        'description': 'Лучший фильм: победители по годам церемонии', 'descriptionEn': 'Best Picture winners by ceremony year',
        'minimum': 90, 'maximum': 110,
        # A film year may precede the ceremony year by 0 to 2 years.
        'lag': (0, 2),
        # Ceremony year -> a word of the winner's English title, as a guard against a changed Wikidata answer.
        'anchors': {2025: 'anora', 2024: 'oppenheimer', 2020: 'parasite', 1998: 'titanic', 1995: 'forrest gump'},
    },
    {
        'id': 'cannes-palme-dor', 'qid': 'Q179808',
        'title': 'Каннский фестиваль', 'titleEn': 'Cannes Film Festival',
        'description': 'Золотая пальмовая ветвь по годам', 'descriptionEn': "Palme d'Or by year",
        'minimum': 70, 'maximum': 100,
        'lag': (0, 1),
        'anchors': {2024: 'anora', 2019: 'parasite', 1994: 'pulp fiction', 2023: 'anatomy of a fall'},
    },
]


def query(qid, relation='P166'):
    """relation: P166 'award received' (winners) or P1411 'nominated for' (nominees)."""
    return f"""
SELECT ?film ?imdb ?labelRu ?labelEn ?original ?published ?date ?poster WHERE {{
  ?film p:{relation} ?statement .
  ?statement ps:{relation} wd:{qid} .
  OPTIONAL {{ ?statement pq:P585 ?date . }}
  ?film wdt:P31/wdt:P279* wd:{FILM_KINDS[0]} .
  ?film wdt:P345 ?imdb .
  FILTER(STRSTARTS(?imdb, "tt"))
  OPTIONAL {{ ?film rdfs:label ?labelRu . FILTER(LANG(?labelRu) = "ru") }}
  OPTIONAL {{ ?film rdfs:label ?labelEn . FILTER(LANG(?labelEn) = "en") }}
  OPTIONAL {{ ?film wdt:P1476 ?original . }}
  OPTIONAL {{ ?film wdt:P577 ?published . }}
  OPTIONAL {{ ?film wdt:P3383 ?poster . }}
}}"""


def fetch(qid, opener=urllib.request.urlopen, sleep=time.sleep, attempts=5, relation='P166'):
    """One SPARQL query. Wikidata answers 429 or 503 when it is busy; waiting as long as it asks (or a growing pause) is the polite way."""
    url = ENDPOINT + '?' + urllib.parse.urlencode({'query': query(qid, relation), 'format': 'json'})
    request = urllib.request.Request(url, headers={'User-Agent': USER_AGENT, 'Accept': 'application/sparql-results+json'})
    for attempt in range(1, attempts + 1):
        try:
            with opener(request, timeout=90) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code not in (429, 502, 503, 504) or attempt == attempts:
                raise
            asked = error.headers.get('Retry-After') if error.headers else None
            pause = int(asked) if asked and asked.isdigit() else 30 * attempt
            print(f'Wikidata answered {error.code}; waiting {pause} s (attempt {attempt} of {attempts})', file=sys.stderr)
            sleep(min(pause, 180))


def imdb_poster(imdb, opener=urllib.request.urlopen):
    """A card-sized poster from IMDb's public title suggestions, the same service the app uses for IMDb-only cards.
    Wikidata knows a poster for few films. Any failure just means the card keeps its plain colour."""
    url = f'https://v3.sg.media-imdb.com/suggestion/{imdb[:1]}/{imdb}.json'
    request = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})
    try:
        with opener(request, timeout=15) as response:
            rows = json.load(response).get('d', [])
    except (OSError, ValueError):
        return None
    for row in rows:
        image = (row.get('i') or {}).get('imageUrl') if isinstance(row, dict) else None
        if isinstance(row, dict) and row.get('id') == imdb and isinstance(image, str) and image.startswith('https://'):
            marker = image.rfind('._V1_')
            return image[:marker] + '._V1_QL75_UX400_.jpg' if marker > 0 and image.lower().endswith('.jpg') else image
    return None


def add_posters(entries, fetch=imdb_poster, pause=0.15):
    """Fills the posters Wikidata lacks; a film that won two awards is asked once."""
    found = {}
    for entry in entries:
        if entry.get('poster'):
            continue
        imdb = entry['imdbId']
        if imdb not in found:
            found[imdb] = fetch(imdb)
            time.sleep(pause)
        if found[imdb]:
            entry['poster'] = found[imdb]


def value(row, name):
    cell = row.get(name)
    return cell.get('value', '').strip() if isinstance(cell, dict) else ''


def year_of(text):
    match = re.match(r'^(-?\d{4})', text or '')
    return int(match.group(1)) if match else 0


def poster_url(text):
    # Wikidata gives a Commons file path; ask for a card-sized copy over https.
    if not text:
        return None
    url = text.replace('http://', 'https://', 1)
    if 'commons.wikimedia.org' not in url:
        return None
    return url + ('&' if '?' in url else '?') + 'width=300'


def entries_from(response, award):
    """One entry per film and year. A film listed twice (several labels or dates) is read once."""
    rows = response.get('results', {}).get('bindings', []) if isinstance(response, dict) else []
    by_key = {}
    for row in rows:
        imdb = value(row, 'imdb')
        year = year_of(value(row, 'date'))
        if not re.fullmatch(r'tt\d{5,10}', imdb) or year == 0:
            continue
        film_year = year_of(value(row, 'published'))
        entry = by_key.setdefault((imdb, year), {
            'year': year, 'imdbId': imdb, 'title': '', 'titleEn': '', 'originalTitle': '', 'filmYear': 0, 'poster': None,
        })
        for field, column in (('title', 'labelRu'), ('titleEn', 'labelEn'), ('originalTitle', 'original')):
            if not entry[field] and value(row, column):
                entry[field] = value(row, column)
        # The earliest publication date is the film's own year.
        if film_year and (not entry['filmYear'] or film_year < entry['filmYear']):
            entry['filmYear'] = film_year
        if not entry['poster']:
            entry['poster'] = poster_url(value(row, 'poster'))
    result = []
    for entry in by_key.values():
        if not entry['originalTitle']:
            entry['originalTitle'] = entry['titleEn']
        if not (entry['title'] or entry['titleEn'] or entry['originalTitle']):
            continue
        if entry['poster'] is None:
            del entry['poster']
        result.append(entry)
    result.sort(key=lambda e: (-e['year'], e['titleEn'] or e['title']))
    return result


def check(award, entries):
    """Raises ValueError when the list does not look like the real one."""
    name = award['id']
    if not award['minimum'] <= len(entries) <= award['maximum']:
        raise ValueError(f"{name}: {len(entries)} entries, expected {award['minimum']}-{award['maximum']}")
    low, high = award['lag']
    for entry in entries:
        if not 1900 <= entry['year'] <= 2100:
            raise ValueError(f"{name}: implausible year {entry['year']} for {entry['imdbId']}")
        if entry['filmYear'] and not low <= entry['year'] - entry['filmYear'] <= high:
            raise ValueError(f"{name}: {entry['imdbId']} won in {entry['year']} but is a {entry['filmYear']} film")
    for year, word in award['anchors'].items():
        names = ' '.join(f"{e['titleEn']} {e['originalTitle']}" for e in entries if e['year'] == year).lower()
        if word not in names:
            raise ValueError(f"{name}: the {year} winner should be '{word}', found '{names.strip()}'")


def nominees_from(response, winners):
    """The nominees of every year, without the film that won that year. Quieter than the winners: no anchors, only plausibility."""
    won = {(e['imdbId'], e['year']) for e in winners}
    result = [e for e in entries_from(response, None) if (e['imdbId'], e['year']) not in won]
    for entry in result:
        if not 1900 <= entry['year'] <= 2100:
            raise ValueError(f"nominee {entry['imdbId']}: implausible year {entry['year']}")
    return result


def build_nominees(responses, winners_document, now=None, posters=None, limit=1500):
    """responses: {award id: Wikidata 'nominated for' response}. Returns the nominees document; an award with none is left out."""
    awards = []
    for award in winners_document['awards']:
        if award['id'] not in responses:
            continue
        entries = nominees_from(responses[award['id']], award['entries'])
        if not entries:
            continue
        if len(entries) > limit:
            raise ValueError(f"{award['id']}: {len(entries)} nominees, expected at most {limit}")
        if posters is not None:
            add_posters(entries, posters)
        awards.append({'id': award['id'], 'entries': entries})
    generated = (now or datetime.datetime.now(datetime.timezone.utc)).strftime('%Y-%m-%dT%H:%M:%SZ')
    return {'schemaVersion': 1, 'generatedAtUtc': generated, 'source': 'Wikidata', 'awards': awards}


def build(responses, now=None, posters=None):
    """responses: {award id: Wikidata response}. Returns the document the app reads.
    posters: a function imdb id -> poster url, used for the entries Wikidata has no poster for."""
    awards = []
    for award in AWARDS:
        if award['id'] not in responses:
            continue
        entries = entries_from(responses[award['id']], award)
        check(award, entries)
        if posters is not None:
            add_posters(entries, posters)
        awards.append({key: award[key] for key in ('id', 'title', 'titleEn', 'description', 'descriptionEn')} | {'entries': entries})
    if not awards:
        raise ValueError('no awards collected')
    generated = (now or datetime.datetime.now(datetime.timezone.utc)).strftime('%Y-%m-%dT%H:%M:%SZ')
    return {'schemaVersion': 1, 'generatedAtUtc': generated, 'source': 'Wikidata', 'awards': awards}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--output', default='awards.json')
    parser.add_argument('--nominees-output', default='awards-nominees.json', help='where the nominees go; empty to skip them')
    parser.add_argument('--no-posters', action='store_true', help='do not ask IMDb for the posters Wikidata lacks')
    parser.add_argument('--from-file', help='a saved Wikidata response instead of the network (needs --award)')
    parser.add_argument('--award', choices=[a['id'] for a in AWARDS])
    args = parser.parse_args(argv)
    responses = {}
    if args.from_file:
        if not args.award:
            parser.error('--from-file needs --award')
        with open(args.from_file, encoding='utf-8') as handle:
            responses[args.award] = json.load(handle)
    else:
        for index, award in enumerate(AWARDS):
            if index:
                time.sleep(10)  # one query after another, not side by side
            responses[award['id']] = fetch(award['qid'])
    document = build(responses, posters=None if args.no_posters else imdb_poster)
    if args.nominees_output and not args.from_file:
        try:
            nominee_responses = {}
            for award in AWARDS:
                time.sleep(10)
                nominee_responses[award['id']] = fetch(award['qid'], relation='P1411')
            nominees = build_nominees(nominee_responses, document, posters=None if args.no_posters else imdb_poster)
            text = json.dumps(nominees, ensure_ascii=False, indent=1) + '\n'
            if len(text.encode('utf-8')) > 1_400_000:
                raise ValueError('awards-nominees.json is too large for the app (limit 1.5 MB)')
            with open(args.nominees_output, 'w', encoding='utf-8', newline='\n') as handle:
                handle.write(text)
            for award in nominees['awards']:
                print(f"{award['id']}: {len(award['entries'])} nominees")
        except (ValueError, OSError, urllib.error.URLError) as error:
            # Winners are the product; missing nominees only mean the year pages show winners alone.
            print(f'warning: nominees skipped: {error}', file=sys.stderr)
    text = json.dumps(document, ensure_ascii=False, indent=1, sort_keys=False) + '\n'
    if len(text.encode('utf-8')) > 400_000:
        raise ValueError('awards.json is too large for the app (limit 512 KB)')
    with open(args.output, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(text)
    for award in document['awards']:
        print(f"{award['id']}: {len(award['entries'])} entries, newest {award['entries'][0]['year']}, oldest {award['entries'][-1]['year']}")
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError) as error:
        print(f'error: {error}', file=sys.stderr)
        sys.exit(1)
