"""Collect a bounded public catalog; publish only complete, validated snapshots."""
import argparse
import datetime
import html
import json
import re
import time
import urllib.request
import urllib.parse
from pathlib import Path

MAX_ITEMS = 15_000
MAX_SNAPSHOT_BYTES = 32 * 1024 * 1024
ITEM_FIELDS = frozenset(['id', 'section', 'title', 'year', 'poster', 'pageUrl',
    'originalTitle', 'description', 'kinopoisk', 'imdb', 'imdbId', 'genre',
    'genreKeys', 'country', 'countryKeys', 'countryKeysComplete', 'people',
    'awards', 'collections', 'metadataCheckedAtUtc', 'metadataUpdatedAtUtc'])


def checked_item(value):
    """Only known text metadata crosses the snapshot boundary."""
    if not isinstance(value, dict) or value.get('section') not in ['movies', 'series']:
        return None
    title, year = value.get('title'), value.get('year')
    if not isinstance(title, str) or not title.strip() or len(title) > 300 or type(year) is not int or not 0 <= year <= 2100:
        return None
    try:
        url = urllib.parse.urlsplit(value.get('pageUrl', ''))
        prefix = '/movies/' if value['section'] == 'movies' else '/tvseries/'
        slug = urllib.parse.unquote(url.path[len(prefix):].rstrip('/'))
        if url.scheme != 'https' or url.netloc != 'w6.zona.plus' or url.query or url.fragment or not url.path.startswith(prefix):
            return None
        if not 1 <= len(slug) <= 120 or not all(c.isalnum() or c == '-' for c in slug) or value.get('id') != value['section'] + ':' + slug:
            return None
    except (TypeError, ValueError):
        return None
    row = {key: item for key, item in value.items() if key in ITEM_FIELDS}
    for field in ['imdb', 'kinopoisk']:
        if field not in row:
            continue
        try:
            score = float(str(row[field]).replace(',', '.'))
            if not 0 < score <= 10:
                row.pop(field)
        except (TypeError, ValueError):
            row.pop(field)
    if 'imdbId' in row and (not isinstance(row['imdbId'], str) or not re.fullmatch(r'tt\d{6,12}', row['imdbId'])):
        row.pop('imdbId')
    for field in ['people', 'awards', 'collections', 'genreKeys', 'countryKeys']:
        if field in row and not isinstance(row[field], list):
            row.pop(field)
    if 'countryKeysComplete' in row and (type(row['countryKeysComplete']) is not bool or not row.get('countryKeys')):
        row.pop('countryKeysComplete')
    if row.get('people'):
        person_fields = ['name', 'role', 'pageUrl', 'profileUrl', 'photoUrl', 'originalName', 'sourcePersonId']
        row['people'] = [{key: person[key] for key in person_fields if key in person}
            for person in row['people'] if isinstance(person, dict) and isinstance(person.get('name'), str)
            and person['name'].strip() and person.get('role') in ['Актёры', 'Режиссёры', 'Операторы']][:80]
        for person in row['people']:
            for field in ['pageUrl', 'profileUrl', 'photoUrl']:
                address = person.get(field)
                if address and (not isinstance(address, str) or not address.startswith('https://')):
                    person.pop(field)
    if row.get('poster') and not str(row['poster']).startswith('https://'):
        row.pop('poster')
    return row


def load_previous(path):
    source = Path(path)
    if source.stat().st_size > MAX_SNAPSHOT_BYTES:
        raise ValueError('Previous catalog is too large')
    previous = json.loads(source.read_text(encoding='utf-8-sig'))
    if isinstance(previous, dict):
        if previous.get('schemaVersion') != 1:
            raise ValueError('Unsupported previous catalog schema')
        previous = previous.get('items')
    if not isinstance(previous, list) or len(previous) > MAX_ITEMS:
        raise ValueError('Invalid previous catalog')
    rows = [checked_item(row) for row in previous]
    if not rows or any(row is None for row in rows):
        raise ValueError('Previous catalog has invalid entries; refusing to reset the library')
    return rows


def merge_catalog(previous, current):
    """Preserve the accumulated library and details when recent pages move on."""
    rows = {row['id']: dict(row) for row in previous}
    for value in current:
        fresh = checked_item(value)
        if fresh is None:
            raise ValueError('Source returned an invalid catalog entry')
        prior = rows.get(fresh['id'])
        if prior and fresh['year'] == 0:
            fresh['year'] = prior['year']
        if prior and (prior['year'] != fresh['year'] or prior['pageUrl'] != fresh['pageUrl']):
            # Never transfer an old identity's credits or scores to a replacement.
            rows[fresh['id']] = fresh
        else:
            rows[fresh['id']] = {**(prior or {}), **{key: item for key, item in fresh.items() if item not in (None, '', [], '—')}}
    if len(rows) > MAX_ITEMS:
        raise ValueError('Accumulated library exceeds the supported limit; previous publication remains usable')
    return list(rows.values())


def snapshot_bytes(feed):
    rows = feed.get('items', [])
    checked = [checked_item(row) for row in rows]
    if not rows or len(rows) > MAX_ITEMS or any(row is None for row in checked):
        raise ValueError('Invalid output catalog')
    if len({row['id'] for row in rows}) != len(rows):
        raise ValueError('Duplicate output identities')
    if sum(row['section'] == 'movies' for row in rows) < 2000 or sum(row['section'] == 'series' for row in rows) < 300:
        raise ValueError('Refusing to publish an incomplete catalog')
    payload = (json.dumps({**feed, 'items': checked}, ensure_ascii=False, separators=(',', ':')) + '\n').encode('utf-8')
    if len(payload) > MAX_SNAPSHOT_BYTES:
        raise ValueError('Text snapshot exceeds the supported limit')
    return payload


def parse_cards(data, section, path):
    rows = []
    for card in re.findall(r'<li\s+class=[\"\']results-item-wrap[\"\'][^>]*>[\s\S]*?</li>', data, re.I):
        def attr(tag, name):
            match = re.search(name + r'=[\"\']([^\"\']+)[\"\']', tag, re.I)
            return html.unescape(match[1]) if match else None
        anchor = re.search(r'<a\b[^>]*itemprop=[\"\']url[\"\'][^>]*>', card, re.I)
        href = attr(anchor[0], 'href') if anchor else None
        name = re.search(r'<[^>]+itemprop=[\"\']name[\"\'][^>]*>([\s\S]*?)</', card, re.I)
        if not href or not href.startswith('/' + path + '/') or not name:
            continue
        slug = href.split('/')[2]
        title = html.unescape(re.sub('<[^>]*>', '', name[1])).strip()
        if not title or not 1 <= len(slug) <= 120 or not all(c.isalnum() or c == '-' for c in slug):
            continue
        year = re.search(r'class=[\"\']results-item-year[\"\'][^>]*>(\d{4})', card, re.I)
        poster = re.search(r'<meta\b[^>]*itemprop=[\"\']image[\"\'][^>]*>', card, re.I)
        image = attr(poster[0], 'content') if poster else None
        rows.append(dict(id=f'{section}:{slug}', section=section, title=title,
                         year=int(year[1]) if year else 0,
                         poster=image if image and re.match(r'^https://img[1-4]\.zonapic\.com/', image) else None,
                         pageUrl=f'https://w6.zona.plus/{path}/{slug}'))
    return rows[:60]


def collect():
    items = {}
    for section, path, pages in [('movies', 'movies', 60), ('series', 'tvseries', 10)]:
        for page in range(1, pages + 1):
            for attempt in range(3):
                try:
                    request = urllib.request.Request(f'https://w6.zona.plus/{path}/filter/sort-date?page={page}',
                        headers={'User-Agent': 'KachalkaCatalog/0.26', 'Accept': 'text/html'})
                    with urllib.request.urlopen(request, timeout=20) as response:
                        raw = response.read(4_000_001)
                    if len(raw) > 4_000_000:
                        raise ValueError('Oversized catalog')
                    rows = parse_cards(raw.decode('utf-8'), section, path)
                    if not rows:
                        raise ValueError('Source returned no catalog cards')
                    break
                except Exception:
                    if attempt == 2:
                        raise
                    time.sleep(2)
            for item in rows:
                items.setdefault(item['id'], item)
            print(section, page, 'cards', len(rows), 'unique', len(items), flush=True)
            time.sleep(.3)
    rows = list(items.values())
    if sum(x['section'] == 'movies' for x in rows) < 2000 or sum(x['section'] == 'series' for x in rows) < 300:
        raise ValueError('Refusing to publish an incomplete catalog')
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--update-seed', action='store_true')
    parser.add_argument('--previous', help='Validated last successful snapshot; defaults to the bundled catalog')
    parser.add_argument('--detail-limit', type=int, default=1000, help='Maximum detail lookups in one daily run')
    parser.add_argument('--metadata-max-age-days', type=int, default=7)
    parser.add_argument('--metadata-budget-seconds', type=int, default=600, help='Stop starting detail requests after this daily budget')
    args = parser.parse_args()
    if not 0 <= args.detail_limit <= 1000 or not 1 <= args.metadata_max_age_days <= 365:
        parser.error('Metadata limits must be 0..1000 records and 1..365 days')
    if not 0 <= args.metadata_budget_seconds <= 1200:
        parser.error('Metadata time budget must be 0..1200 seconds')
    previous_path = Path(args.previous) if args.previous else Path(__file__).resolve().parents[1] / 'web-index/app/data/seed.json'
    previous = load_previous(previous_path)
    rows = merge_catalog(previous, collect())
    now = datetime.datetime.now(datetime.timezone.utc)
    rows.sort(key=lambda row: min(row['year'], now.year), reverse=True)
    from library_metadata import enrich
    rows = enrich(rows, limit=args.detail_limit, max_age_days=args.metadata_max_age_days,
                  budget_seconds=args.metadata_budget_seconds, now=now)
    feed = dict(schemaVersion=1, generatedAtUtc=now.isoformat().replace('+00:00', 'Z'),
                source='https://w6.zona.plus', metadataVersion=2, items=rows,
                metadataCoverage=dict(descriptions=sum(bool(row.get('description')) for row in rows),
                    credits=sum(bool(row.get('people')) for row in rows),
                    imdbScores=sum(bool(row.get('imdb')) for row in rows)))
    from catalog_collections import attach_collections
    attach_collections(feed, Path(__file__).with_name('catalog-collections.json'), now)
    payload = snapshot_bytes(feed)
    target = Path(args.output)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp = target.with_suffix('.tmp')
    temp.write_bytes(payload)
    temp.replace(target)
    from catalog_snapshot import publish_snapshot
    publish_snapshot(target)
    if args.update_seed:
        data = Path(__file__).resolve().parents[1] / 'web-index/app/data'
        (data / 'seed.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        starter = [x for section in ['movies', 'series'] for x in [r for r in rows if r['section'] == section][:40]]
        (data / 'starter.json').write_text(json.dumps(starter, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Complete catalog:', len(rows), 'entries;', feed['generatedAtUtc'], flush=True)


if __name__ == '__main__':
    main()

