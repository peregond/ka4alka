"""TMDB as a second source for the shared library.

Every title with an IMDb id gets its TMDB id and cover path (kept for later). Its Russian description,
genres and countries are stored only where the library has none of its own. Needs TMDB_API_KEY (a v3
key or a v4 read token); without it nothing is requested.
"""
import concurrent.futures
import datetime
import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request

API = 'https://api.themoviedb.org/3'
KIND = {'movies': 'movie', 'series': 'tv'}


class Stop(Exception):
    """The service refuses further requests (bad key or throttled)."""


def fetch(path, token, **params):
    headers = {'Accept': 'application/json', 'User-Agent': 'KachalkaCatalog/0.45'}
    if len(token) > 40:
        headers['Authorization'] = 'Bearer ' + token
    else:
        params['api_key'] = token
    url = f'{API}{path}?{urllib.parse.urlencode(params)}'
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=20) as response:
            data = response.read(3_000_001)
    except urllib.error.HTTPError as error:
        if error.code in (401, 429):
            raise Stop(error.code)
        if error.code == 404:
            return None
        raise
    if len(data) > 3_000_000:
        raise ValueError('Oversized TMDB answer')
    return json.loads(data)


def now_text(now):
    return now.isoformat().replace('+00:00', 'Z')


def stamp(row, field):
    try:
        value = datetime.datetime.fromisoformat(row.get(field, '').replace('Z', '+00:00'))
        return value if value.tzinfo else None
    except (ValueError, TypeError, AttributeError):
        return None


def find_rows(rows, limit, now, retry_days=30):
    """Titles with an IMDb id and no TMDB id, the newest first; one that was not found waits a month."""
    horizon = now - datetime.timedelta(days=retry_days)

    def waiting(row):
        checked = stamp(row, 'tmdbCheckedAtUtc')
        return bool(checked and checked > horizon)

    wanted = [row for row in rows if re.fullmatch(r'tt\d{6,12}', str(row.get('imdbId'))) and row.get('section') in KIND
              and not row.get('tmdbId') and not waiting(row)]
    return [row['id'] for row in sorted(wanted, key=lambda row: (-row['year'], row['id']))[:limit]]


def card_rows(rows, limit):
    """Titles with a TMDB id whose card was never read."""
    wanted = [row for row in rows if type(row.get('tmdbId')) is int and row['tmdbId'] > 0 and not row.get('tmdbTextAtUtc')]
    return [row['id'] for row in sorted(wanted, key=lambda row: (-row['year'], row['id']))[:limit]]


def clean(value, limit):
    if not isinstance(value, str):
        return None
    text = re.sub(r'[ \t\xa0]+', ' ', value).strip()
    return text[:limit] if text else None


def names(items, key, limit, lower=False):
    values = [clean(item.get(key), 60) for item in items or [] if isinstance(item, dict)]
    joined = ', '.join(value.lower() if lower else value for value in values if value)[:limit]
    return joined or None


def card_fields(row, card):
    """What a TMDB card adds: its cover path always, its texts only where the title has none."""
    found = {}
    path = card.get('poster_path')
    if isinstance(path, str) and re.fullmatch(r'/[A-Za-z0-9_-]{5,60}\.(jpg|png)', path):
        found['tmdbPoster'] = path
    texts = (('description', 'kinopoiskDescription', 'tmdbOverview', clean(card.get('overview'), 2500)),
             ('genre', 'kinopoiskGenre', 'tmdbGenre', names(card.get('genres'), 'name', 120, lower=True)),
             ('country', 'kinopoiskCountry', 'tmdbCountry', names(card.get('production_countries'), 'name', 160)))
    for own, other, key, text in texts:
        if text and not (row.get(own) or '').strip() and not row.get(other):
            found[key] = text
    return found


def enrich(rows, token, now=None, get=fetch, finds=4000, cards=4000, budget_seconds=300, workers=4):
    """Returns new rows. A refused key or throttling stops the run; previous data stays."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    if not token:
        return rows
    deadline = time.monotonic() + max(0, budget_seconds)
    by_id = {row['id']: row for row in rows}
    stats = dict(found=0, looked=0, cards=0, failed=0, stopped=None)

    def run(ids, work):
        results = {}
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
            pending = iter(ids)
            running = {}
            while True:
                while len(running) < workers and time.monotonic() < deadline and not stats['stopped']:
                    item = next(pending, None)
                    if item is None:
                        break
                    running[pool.submit(work, by_id[item])] = item
                if not running:
                    break
                done, _ = concurrent.futures.wait(running, return_when=concurrent.futures.FIRST_COMPLETED)
                for future in done:
                    item = running.pop(future)
                    try:
                        results[item] = future.result()
                    except Stop as error:
                        stats['stopped'] = error.args[0]
                    except Exception:
                        stats['failed'] += 1
        return results

    def find(row):
        kind = KIND[row['section']]
        hits = (get(f"/find/{row['imdbId']}", token, external_source='imdb_id') or {}).get(kind + '_results') or []
        return hits[0]['id'] if len(hits) == 1 and type(hits[0].get('id')) is int and hits[0]['id'] > 0 else 0

    found = run(find_rows(rows, finds, now), find)
    stats['looked'] = len(found)
    rows = [dict(row, tmdbCheckedAtUtc=now_text(now), **({'tmdbId': found[row['id']]} if found[row['id']] else {})) if row['id'] in found else row for row in rows]
    by_id = {row['id']: row for row in rows}

    def read(row):
        data = get(f"/{KIND[row['section']]}/{row['tmdbId']}", token, language='ru-RU')
        return card_fields(row, data) if isinstance(data, dict) else {}

    read_cards = run(card_rows(rows, cards), read)
    stats['cards'] = len(read_cards)
    rows = [dict(row, tmdbTextAtUtc=now_text(now), **read_cards[row['id']]) if row['id'] in read_cards else row for row in rows]
    stats['found'] = sum(bool(row.get('tmdbId')) for row in rows)
    print('TMDB:', stats['looked'], 'looked up;', stats['cards'], 'cards read;', stats['found'], 'titles with a TMDB id;',
          stats['failed'], 'failed' + (f"; stopped by HTTP {stats['stopped']}" if stats['stopped'] else ''), flush=True)
    return rows
