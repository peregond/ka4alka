"""Could TMDB replace the source of the library's list of new titles, Russian names and cast photos? A probe only.

Needs TMDB_API_KEY (a v3 key or a v4 read token). Reads the published catalog; nothing is written to it.
"""
import argparse
import concurrent.futures
import datetime
import json
import os
import random
import re
import urllib.parse
import urllib.request

API = 'https://api.themoviedb.org/3'
KIND = {'movies': 'movie', 'series': 'tv'}


def get(path, token, **params):
    headers = {'Accept': 'application/json', 'User-Agent': 'KachalkaProbe/1'}
    if len(token) > 40:
        headers['Authorization'] = 'Bearer ' + token
    else:
        params['api_key'] = token
    url = f'{API}{path}?{urllib.parse.urlencode(params)}'
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=20) as response:
        return json.loads(response.read(3_000_000))


def tmdb_id(row, token, fetch=get):
    """TMDB id of a library title through its IMDb id; None when TMDB does not know it exactly once."""
    if not row.get('imdbId'):
        return None
    hits = fetch(f"/find/{row['imdbId']}", token, external_source='imdb_id').get(KIND[row['section']] + '_results') or []
    return hits[0]['id'] if len(hits) == 1 else None


def popular_recent(section, token, since, pages=10, fetch=get):
    """TMDB's most popular titles released since `since`, as (id, shown title, original title)."""
    kind = KIND[section]
    date = 'primary_release_date.gte' if kind == 'movie' else 'first_air_date.gte'
    found = []
    for page in range(1, pages + 1):
        data = fetch(f'/discover/{kind}', token, sort_by='popularity.desc', page=page, **{date: since.isoformat()})
        for item in data.get('results') or []:
            found.append((item['id'], item.get('title') or item.get('name') or '', item.get('original_title') or item.get('original_name') or ''))
        if page >= (data.get('total_pages') or 1):
            break
    return found


def feed_report(section, library_ids, top):
    """How much of TMDB's popular recent list the library already has, and what it lacks."""
    have = [item for item in top if item[0] in library_ids]
    missing = [item for item in top if item[0] not in library_ids]
    return dict(section=section, top=len(top), inside=len(have),
                share=100 * len(have) // max(1, len(top)),
                first_missing=[f'{title} / {original}' for _, title, original in missing[:25]])


def safe(function, *arguments, **keywords):
    """A failed request is only a missing answer."""
    try:
        return function(*arguments, **keywords)
    except Exception:
        return None


def cyrillic(text):
    return bool(re.search('[а-яА-ЯёЁ]', text or ''))


def names_and_people(row, card):
    """Russian title agreement and cast photos for one title."""
    shown = card.get('title') or card.get('name') or ''
    original = card.get('original_title') or card.get('original_name') or ''
    cast = (card.get('credits') or {}).get('cast') or []
    return dict(has_ru_title=cyrillic(shown), same_as_library=shown.strip().casefold() == row['title'].strip().casefold(),
                translated=shown != original, cast=len(cast[:8]),
                with_photo=sum(bool(person.get('profile_path')) for person in cast[:8]),
                ru_names=sum(cyrillic(person.get('name')) for person in cast[:8]))


def summarize_names(parts):
    parts = [part for part in parts if part]
    if not parts:
        return 'no titles'
    count = len(parts)
    cast = sum(part['cast'] for part in parts)
    return (f"{count} titles: Russian title present {100 * sum(p['has_ru_title'] for p in parts) // count}%, "
            f"equal to the library title {100 * sum(p['same_as_library'] for p in parts) // count}%; "
            f"cast photos {100 * sum(p['with_photo'] for p in parts) // max(1, cast)}%, "
            f"cast names in Cyrillic {100 * sum(p['ru_names'] for p in parts) // max(1, cast)}%")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--catalog', required=True)
    parser.add_argument('--report', required=True)
    parser.add_argument('--top', type=int, default=10, help='pages of 20 titles of TMDB popular list per section')
    parser.add_argument('--names-sample', type=int, default=150)
    args = parser.parse_args()
    token = os.environ.get('TMDB_API_KEY', '').strip()
    if not token:
        raise SystemExit('TMDB_API_KEY is not set')
    rows = json.load(open(args.catalog, encoding='utf-8'))['items']
    lines = []
    pool = concurrent.futures.ThreadPoolExecutor(max_workers=4)

    since = datetime.date.today() - datetime.timedelta(days=548)
    report = dict(feed=[])
    for section in ('movies', 'series'):
        part = [row for row in rows if row['section'] == section]
        ids = set(filter(None, pool.map(lambda row: safe(tmdb_id, row, token), part)))
        top = popular_recent(section, token, since, args.top)
        result = feed_report(section, ids, top)
        report['feed'].append(result)
        lines.append(f"{section}: library maps {len(ids)} of {len(part)} titles to TMDB; TMDB's {result['top']} most popular "
                     f"since {since}: {result['inside']} already in the library ({result['share']}%)")
        lines.append('  popular titles the library lacks: ' + '; '.join(result['first_missing'][:12]))
    sample = [row for row in rows if row.get('imdbId')]
    random.Random(11).shuffle(sample)

    def card(row):
        found = safe(tmdb_id, row, token)
        if not found:
            return None
        data = safe(get, f"/{KIND[row['section']]}/{found}", token, language='ru-RU', append_to_response='credits')
        return names_and_people(row, data) if data else None

    parts = list(pool.map(card, sample[:args.names_sample]))
    lines.append('names and people: ' + summarize_names(parts))
    report['names'] = summarize_names(parts)
    text = '\n'.join(lines)
    print(text, flush=True)
    with open(args.report, 'w', encoding='utf-8') as handle:
        json.dump(dict(summary=text, **report), handle, ensure_ascii=False, indent=1)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as handle:
            handle.write('### TMDB list of new titles, Russian names and cast photos\n```\n' + text + '\n```\n')


if __name__ == '__main__':
    main()
