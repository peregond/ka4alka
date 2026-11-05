"""How much of the shared library TMDB can describe on its own: a sample probe, never part of the catalog.

Needs a TMDB token in TMDB_API_KEY (a v3 key or a v4 read token). Reads the published catalog,
asks TMDB for a sample of titles (by IMDb id when the title has one, otherwise by name and year)
and reports which fields come back in Russian. Nothing is written to the library.
"""
import argparse
import collections
import concurrent.futures
import json
import os
import random
import urllib.error
import urllib.parse
import urllib.request

API = 'https://api.themoviedb.org/3'
FIELDS = ('overview', 'poster', 'genres', 'countries', 'cast', 'directors', 'imdb_id', 'vote')


def get(path, token, **params):
    headers = {'Accept': 'application/json', 'User-Agent': 'KachalkaProbe/1'}
    if len(token) > 40:
        headers['Authorization'] = 'Bearer ' + token
    else:
        params['api_key'] = token
    url = f'{API}{path}?{urllib.parse.urlencode(params)}'
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=20) as response:
        return json.loads(response.read(3_000_000))


def describe(row, token, fetch=get):
    """Which fields TMDB gives for one title; None when TMDB does not know it."""
    kind = 'movie' if row['section'] == 'movies' else 'tv'
    found = None
    how = None
    if row.get('imdbId'):
        data = fetch(f"/find/{row['imdbId']}", token, external_source='imdb_id', language='ru-RU')
        hits = data.get(kind + '_results') or []
        found = hits[0]['id'] if len(hits) == 1 else None
        how = 'imdb'
    elif row.get('title') and row.get('year'):
        key = 'year' if kind == 'movie' else 'first_air_date_year'
        hits = fetch(f'/search/{kind}', token, query=row['title'], language='ru-RU', **{key: row['year']}).get('results') or []
        found = hits[0]['id'] if len(hits) == 1 else None
        how = 'name'
    if found is None:
        return None
    card = fetch(f'/{kind}/{found}', token, language='ru-RU', append_to_response='credits,external_ids')
    credits = card.get('credits') or {}
    crew = credits.get('crew') or []
    return dict(how=how, tmdb=found,
                overview=bool((card.get('overview') or '').strip()), poster=bool(card.get('poster_path')),
                genres=bool(card.get('genres')),
                countries=bool(card.get('production_countries') or card.get('origin_country')),
                cast=len(credits.get('cast') or []) >= 3,
                directors=any(c.get('job') == 'Director' for c in crew) or kind == 'tv',
                imdb_id=bool((card.get('external_ids') or {}).get('imdb_id') or card.get('imdb_id')),
                vote=bool(card.get('vote_average')))


def summarize(results):
    """results: list of (row, description or None)."""
    lines = []
    for label, pick in (('all', lambda row: True), ('with IMDb id', lambda row: bool(row.get('imdbId'))),
                        ('without IMDb id', lambda row: not row.get('imdbId'))):
        part = [(row, card) for row, card in results if pick(row)]
        if not part:
            continue
        known = [card for _, card in part if card]
        lines.append(f"{label}: {len(part)} sampled, found {len(known)} ({100 * len(known) // len(part)}%)")
        lines.append('  fields of found titles: ' + ', '.join(
            f"{name} {100 * sum(card[name] for card in known) // max(1, len(known))}%" for name in FIELDS))
    by_year = collections.defaultdict(lambda: [0, 0])
    for row, card in results:
        bucket = by_year[min(row['year'], 2026) // 5 * 5]
        bucket[0] += 1
        bucket[1] += bool(card)
    lines.append('found by five-year period: ' + ', '.join(f'{year}s {found}/{total}' for year, (total, found) in sorted(by_year.items())))
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--catalog', required=True)
    parser.add_argument('--sample', type=int, default=300)
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    token = os.environ.get('TMDB_API_KEY', '').strip()
    if not token:
        raise SystemExit('TMDB_API_KEY is not set')
    rows = [row for row in json.load(open(args.catalog, encoding='utf-8'))['items'] if type(row.get('year')) is int and row['year'] > 0]
    random.Random(7).shuffle(rows)
    sample = rows[:args.sample]

    def one(row):
        try:
            return row, describe(row, token)
        except urllib.error.HTTPError as error:
            return row, dict(error=f'HTTP {error.code}')
        except Exception as error:
            return row, dict(error=type(error).__name__)

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(one, sample))
    errors = [card['error'] for _, card in results if card and 'error' in card]
    results = [(row, None if card and 'error' in card else card) for row, card in results]
    text = summarize(results) + (f'\nrequests failed: {len(errors)}, first: {errors[0]}' if errors else '')
    print(text, flush=True)
    with open(args.report, 'w', encoding='utf-8') as handle:
        json.dump(dict(sample=len(sample), errors=len(errors), summary=text,
                       results=[dict(id=row['id'], title=row['title'], year=row['year'], card=card) for row, card in results]),
                  handle, ensure_ascii=False, indent=1)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as handle:
            handle.write('### TMDB coverage of the library\n```\n' + text + '\n```\n')


if __name__ == '__main__':
    main()
