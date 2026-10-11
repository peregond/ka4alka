"""Bounded metadata enrichment; IMDb scores come from its official public dataset."""
import concurrent.futures
import datetime
import gzip
import io
import json
import re
import time
import unicodedata
import urllib.parse
import urllib.request

RATINGS = 'https://datasets.imdbws.com/title.ratings.tsv.gz'
# IMDb uses the international title Crescendo for this verified French title.
ALIASES = {('l objet du delit', 2026): 'tt37535559'}


def normalize(value):
    value = ''.join(c for c in unicodedata.normalize('NFKD', value.casefold()) if not unicodedata.combining(c))
    return re.sub(r'[^\w]+', ' ', value).strip()


def read(url, maximum=4_000_000, headers=None):
    request = urllib.request.Request(url, headers={'User-Agent': 'KachalkaCatalog/0.40', **(headers or {})})
    with urllib.request.urlopen(request, timeout=12) as response:
        data = response.read(maximum+1)
    if len(data) > maximum:
        raise ValueError('Oversized metadata')
    return data


def parse_detail(text):
    from catalog_text import parse_html_metadata
    return parse_html_metadata(text)


def imdb_identity(item, data):
    original = normalize(item.get('originalTitle', ''))
    matches = []
    for row in data.get('d', []):
        if not re.fullmatch(r'tt\d{6,12}', row.get('id', '')) or row.get('y') != item['year']:
            continue
        kind = row.get('qid')
        if item['section'] == 'movies' and kind != 'movie' or item['section'] == 'series' and kind not in ['tvSeries', 'tvMiniSeries']:
            continue
        if normalize(row.get('l', '')) == original or ALIASES.get((original, item['year'])) == row['id']:
            matches.append(row['id'])
    return matches[0] if len(set(matches)) == 1 else None


def merge_metadata(prior, fresh):
    from catalog_text import merge_people
    merged = dict(prior)
    for key, value in fresh.items():
        if value in (None, '', [], '—'):
            continue
        if key == 'people':
            merged[key] = merge_people(prior.get('people') or [], value)
        elif key in ['country', 'countryKeys', 'countryKeysComplete'] and prior.get('countryKeysComplete') and not fresh.get('countryKeysComplete'):
            continue
        else:
            merged[key] = value
    return merged


def enrich_one(item, now=None):
    from catalog_text import parse_text_metadata
    now = now or datetime.datetime.now(datetime.timezone.utc)
    row = dict(item)
    metadata = {}
    try:
        data = read(row['pageUrl'], headers={'Accept': 'application/json, text/javascript, */*; q=0.01', 'X-Requested-With': 'XMLHttpRequest'})
        metadata = parse_text_metadata(data, row)
    except Exception:
        pass
    if not metadata.get('description') or not metadata.get('people'):
        try:
            html_metadata = parse_text_metadata(read(row['pageUrl'], headers={'Accept': 'text/html'}), row)
            # Rich JSON identities stay authoritative when an HTML fallback is sparse.
            metadata = merge_metadata(html_metadata, metadata)
        except Exception:
            pass
    row = merge_metadata(row, metadata)
    row['metadataCheckedAtUtc'] = now.isoformat().replace('+00:00', 'Z')
    if metadata:
        row['metadataUpdatedAtUtc'] = row['metadataCheckedAtUtc']
    original = row.get('originalTitle')
    if original and not re.search('[А-Яа-яЁё]', original) and not row.get('imdbId'):
        try:
            query = urllib.parse.quote(normalize(original))
            match = imdb_identity(row, json.loads(read('https://v3.sg.media-imdb.com/suggestion/x/'+query+'.json', 500_000)))
            if match:
                row['imdbId'] = match
        except Exception:
            pass
    return row


def official_scores(data, ids):
    scores = {}
    with gzip.GzipFile(fileobj=io.BytesIO(data)) as stream:
        total = 0
        for line in stream:
            total += len(line)
            if total > 150_000_000:
                raise ValueError('Oversized decompressed ratings')
            parts = line.decode('ascii').strip().split('\t')
            if len(parts) != 3 or parts[0] not in ids:
                continue
            score, votes = float(parts[1]), int(parts[2])
            if 0 < score <= 10 and votes > 0:
                scores[parts[0]] = parts[1]
    return scores


def select_details(rows, limit=1000, now=None, max_age_days=7):
    """Each attempted record moves behind untouched and older records, including failures."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    oldest = datetime.datetime.min.replace(tzinfo=datetime.timezone.utc)
    def checked(row):
        try:
            stamp = datetime.datetime.fromisoformat(row.get('metadataCheckedAtUtc', '').replace('Z', '+00:00'))
            return stamp.astimezone(datetime.timezone.utc) if stamp.tzinfo and stamp <= now + datetime.timedelta(minutes=15) else oldest
        except (ValueError, TypeError, AttributeError):
            return oldest
    stale = [row for row in rows if checked(row) <= now - datetime.timedelta(days=max_age_days)]
    return sorted(stale, key=lambda row: (checked(row), -row['year'], row['id']))[:limit]


def enrich(rows, limit=1000, now=None, max_age_days=7, budget_seconds=600):
    now = now or datetime.datetime.now(datetime.timezone.utc)
    selected = select_details(rows, limit, now, max_age_days)
    deadline = time.monotonic() + max(0, budget_seconds)
    updates = {}
    waiting = iter(selected)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        pending = {}
        def submit_available():
            while len(pending) < 4 and time.monotonic() < deadline:
                row = next(waiting, None)
                if row is None:
                    break
                pending[pool.submit(enrich_one, row, now)] = row
        submit_available()
        while pending:
            done, _ = concurrent.futures.wait(pending, return_when=concurrent.futures.FIRST_COMPLETED)
            for future in done:
                prior = pending.pop(future)
                try:
                    fresh = future.result()
                except Exception:
                    # A failed parser/request still rotates its attempted record,
                    # while its previous verified metadata remains available.
                    fresh = {**prior, 'metadataCheckedAtUtc': now.isoformat().replace('+00:00', 'Z')}
                updates[prior['id']] = fresh
            # Only four started requests can remain after the deadline. No
            # prequeued work keeps crawling beyond the daily time budget.
            submit_available()
    rows = [updates.get(x['id'], x) for x in rows]
    ids = {x['imdbId'] for x in rows if isinstance(x.get('imdbId'), str) and re.fullmatch(r'tt\d{6,12}', x['imdbId'])}
    if ids:
        try:
            scores = official_scores(read(RATINGS, 20_000_000), ids)
            for row in rows:
                if row.get('imdbId') in scores:
                    row['imdb'] = scores[row['imdbId']]
        except Exception as error:
            print('IMDb refresh unavailable; confirmed previous scores preserved:', type(error).__name__, flush=True)
    print('Metadata:', len(updates), 'detail checks;', sum(bool(x.get('description')) for x in rows), 'descriptions;', sum(bool(x.get('people')) for x in rows), 'credits;', sum(bool(x.get('imdb')) for x in rows), 'IMDb scores', flush=True)
    return rows

