"""Kinopoisk scores for the shared library from kinopoiskapiunofficial.tech.

The service needs a personal token (secret KINOPOISK_API_TOKEN). Without it
nothing is requested and the previous scores stay untouched. Titles are matched
only through their IMDb identifier, so a similarly named film is never scored.
"""
import datetime
import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = 'https://kinopoiskapiunofficial.tech/api/v2.2/films'
STOP_STATUSES = (401, 402, 429)  # bad token, daily quota spent, throttled: nothing more today


class QuotaReached(Exception):
    pass


def fetch(url, token):
    request = urllib.request.Request(url, headers={'X-API-KEY': token, 'Accept': 'application/json',
                                                   'User-Agent': 'KachalkaCatalog/0.45'})
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            data = response.read(2_000_001)
    except urllib.error.HTTPError as error:
        if error.code in STOP_STATUSES:
            raise QuotaReached(error.code)
        if error.code == 404:
            return None
        raise
    if len(data) > 2_000_000:
        raise ValueError('Oversized Kinopoisk answer')
    return json.loads(data)


def score(value):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return f'{number:.1f}' if 0 < number <= 10 else None


def lookup(row, token, get=fetch):
    """Returns (kinopoiskId, score) for a row; either may be None."""
    known = row.get('kinopoiskId')
    if type(known) is int and known > 0:
        data = get(f'{BASE}/{known}', token)
        return (known, score(data.get('ratingKinopoisk'))) if isinstance(data, dict) else (known, None)
    imdb = row.get('imdbId')
    if not isinstance(imdb, str) or not re.fullmatch(r'tt\d{6,12}', imdb):
        return None, None
    data = get(f'{BASE}?{urllib.parse.urlencode({"imdbId": imdb})}', token)
    items = [x for x in (data or {}).get('items', []) if isinstance(x, dict) and x.get('imdbId') == imdb
             and type(x.get('kinopoiskId')) is int and x['kinopoiskId'] > 0]
    if len({x['kinopoiskId'] for x in items}) != 1:
        return None, None
    return items[0]['kinopoiskId'], score(items[0].get('ratingKinopoisk'))


def stamp(row):
    try:
        value = datetime.datetime.fromisoformat(row.get('kinopoiskCheckedAtUtc', '').replace('Z', '+00:00'))
        return value if value.tzinfo else None
    except (ValueError, TypeError, AttributeError):
        return None


def select_rows(rows, limit, now, max_age_days=7):
    """Titles with a known identity, never scored first, then the longest unchecked."""
    oldest = datetime.datetime.min.replace(tzinfo=datetime.timezone.utc)
    horizon = now - datetime.timedelta(days=max_age_days)
    candidates = []
    for row in rows:
        if not (row.get('imdbId') or row.get('kinopoiskId')):
            continue
        checked = stamp(row) or oldest
        if checked > now + datetime.timedelta(minutes=15) or checked > horizon:
            continue
        candidates.append((bool(row.get('kinopoisk')), checked, row['id']))
    return [row_id for _, _, row_id in sorted(candidates)[:limit]]


def refresh(rows, token, limit=450, now=None, get=fetch, pause=0.06):
    """Scores for up to `limit` titles; returns new rows and the number of lookups made."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    if not token or limit <= 0:
        return rows, 0
    wanted = set(select_rows(rows, limit, now))
    done = 0
    result = []
    stopped = None
    for row in rows:
        if row['id'] not in wanted or stopped:
            result.append(row)
            continue
        try:
            found_id, found_score = lookup(row, token, get)
        except QuotaReached as error:
            stopped = error.args[0]
            result.append(row)
            continue
        except Exception:
            # A failed attempt moves behind untouched titles; its old score stays.
            found_id, found_score = row.get('kinopoiskId'), None
        done += 1
        fresh = dict(row, kinopoiskCheckedAtUtc=now.isoformat().replace('+00:00', 'Z'))
        if found_id:
            fresh['kinopoiskId'] = found_id
        if found_score:
            fresh['kinopoisk'] = found_score
        result.append(fresh)
        time.sleep(pause)
    if stopped:
        print('Kinopoisk stopped by HTTP', stopped, 'after', done, 'lookups; previous scores preserved', flush=True)
    print('Kinopoisk:', done, 'lookups;', sum(bool(x.get('kinopoisk')) for x in result), 'scores', flush=True)
    return result, done
