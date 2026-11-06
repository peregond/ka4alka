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

from library_metadata import normalize

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


def clean(value, limit):
    """A short Russian text from the service, or None."""
    if not isinstance(value, str):
        return None
    text = re.sub(r'[ \t\xa0]+', ' ', value).strip()
    return text[:limit] if text else None


def film_text(data, current_description=None):
    """The Russian texts of a film card; a description equal to the one already stored is not stored twice."""
    found = {}
    if name := clean(data.get('nameRu'), 200):
        found['kinopoiskTitle'] = name
    description = clean(data.get('description'), 2500)
    if description and description != (current_description or '').strip():
        found['kinopoiskDescription'] = description
    for key, field, limit, names in (('kinopoiskGenre', 'genres', 120, 'genre'), ('kinopoiskCountry', 'countries', 160, 'country')):
        values = [clean(item.get(names), 60) for item in data.get(field) or [] if isinstance(item, dict)]
        if joined := ', '.join(value for value in values if value)[:limit]:
            found[key] = joined
    return found


def needs_text(row, now=None):
    """A recent film, or one with no description of its own, whose card was never read for its Russian texts.

    Older films already hold their texts in the library; the card is not spent on them."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    return (type(row.get('kinopoiskId')) is int and row['kinopoiskId'] > 0 and not row.get('kinopoiskTextAtUtc')
            and (row['year'] >= now.year - 1 or not (row.get('description') or '').strip()))


def details(row, token, get=fetch):
    """The card of a film with a known id: (id, score, texts)."""
    known = row['kinopoiskId']
    data = get(f'{BASE}/{known}', token)
    if not isinstance(data, dict):
        return known, None, {}
    return known, score(data.get('ratingKinopoisk')), film_text(data, row.get('description'))


def fill_missing_texts(rows):
    """A title that lacks its description, genres or countries shows the Kinopoisk ones.

    The app reads these fields as they are, so no update of the app is needed."""
    pairs = (('description', 'kinopoiskDescription'), ('genre', 'kinopoiskGenre'), ('country', 'kinopoiskCountry'))
    filled = []
    for row in rows:
        gaps = {field: row[source] for field, source in pairs if not (row.get(field) or '').strip() and row.get(source)}
        filled.append(dict(row, **gaps) if gaps else row)
    return filled


def lookup(row, token, get=fetch):
    """Returns (kinopoiskId, score) for a row; either may be None."""
    known = row.get('kinopoiskId')
    if type(known) is int and known > 0:
        return details(row, token, get)[:2]
    imdb = row.get('imdbId')
    if not isinstance(imdb, str) or not re.fullmatch(r'tt\d{6,12}', imdb):
        return search(row, token, get)
    data = get(f'{BASE}?{urllib.parse.urlencode({"imdbId": imdb})}', token)
    items = [x for x in (data or {}).get('items', []) if isinstance(x, dict) and x.get('imdbId') == imdb
             and type(x.get('kinopoiskId')) is int and x['kinopoiskId'] > 0]
    if len({x['kinopoiskId'] for x in items}) != 1:
        return None, None
    return items[0]['kinopoiskId'], score(items[0].get('ratingKinopoisk'))


KINDS = {'movies': ('FILM',), 'series': ('TV_SERIES', 'MINI_SERIES')}


def search(row, token, get=fetch):
    """For a title without any identity: the one entry of the same name, year and kind."""
    title, year = row.get('title'), row.get('year')
    if not isinstance(title, str) or not normalize(title) or type(year) is not int or year <= 0 or row.get('section') not in KINDS:
        return None, None
    query = urllib.parse.urlencode({'keyword': title, 'yearFrom': year, 'yearTo': year, 'order': 'NUM_VOTE'})
    data = get(f'{BASE}?{query}', token)
    wanted = {normalize(row[field]) for field in ('title', 'originalTitle') if isinstance(row.get(field), str)} - {''}
    items = {}
    for item in (data or {}).get('items', []):
        if not isinstance(item, dict) or item.get('year') != year or item.get('type') not in KINDS[row['section']]:
            continue
        names = {normalize(item[field]) for field in ('nameRu', 'nameOriginal', 'nameEn') if isinstance(item.get(field), str)}
        if type(item.get('kinopoiskId')) is int and item['kinopoiskId'] > 0 and wanted & names:
            items[item['kinopoiskId']] = item
    if len(items) != 1:
        return None, None
    found = next(iter(items.values()))
    return found['kinopoiskId'], score(found.get('ratingKinopoisk'))


def stamp(row):
    try:
        value = datetime.datetime.fromisoformat(row.get('kinopoiskCheckedAtUtc', '').replace('Z', '+00:00'))
        return value if value.tzinfo else None
    except (ValueError, TypeError, AttributeError):
        return None


def searchable(row):
    return bool(row.get('title')) and type(row.get('year')) is int and row['year'] > 0 and row.get('section') in KINDS


def settled(row, now):
    """A score of a film from before last year is final: it is kept as is and never asked again."""
    return bool(row.get('kinopoisk') and row.get('kinopoiskId')) and row['year'] < now.year - 1 and not needs_text(row, now)


def select_rows(rows, limit, now, max_age_days=7, retry_days=30, text_share=25):
    """Weekly for this and last year's titles; older ones only until they have a score.

    Never scored titles go first. Old titles that could not be scored are retried monthly.
    A few requests of every run are kept for cards never read for their texts, so that the
    texts accumulate (and the card format is confirmed) while the unscored titles are served."""
    oldest = datetime.datetime.min.replace(tzinfo=datetime.timezone.utc)
    candidates = []
    for row in rows:
        if settled(row, now) or not (row.get('imdbId') or row.get('kinopoiskId') or searchable(row)):
            continue
        recent = row['year'] >= now.year - 1
        checked = stamp(row) or oldest
        # A card never read for its texts is read at once, whatever the cooldown.
        if not needs_text(row, now) and (checked > now + datetime.timedelta(minutes=15) or checked > now - datetime.timedelta(days=max_age_days if recent else retry_days)):
            continue
        candidates.append((bool(row.get('kinopoisk')), checked, row['id'], needs_text(row, now)))
    ordered = sorted(candidates)
    texts = [item for item in ordered if item[3]][:max(0, min(text_share, limit))]
    chosen = {item[2] for item in texts}
    rest = [item for item in ordered if item[2] not in chosen]
    return [item[2] for item in (texts + rest)[:limit]]


def refresh(rows, token, limit=450, now=None, get=fetch, pause=0.06):
    """Scores for up to `limit` titles; returns new rows and the number of lookups made."""
    now = now or datetime.datetime.now(datetime.timezone.utc)
    if not token or limit <= 0:
        return rows, 0
    wanted = set(select_rows(rows, limit, now))
    done = 0
    failures = []
    result = []
    stopped = None
    for row in rows:
        if row['id'] not in wanted or stopped:
            result.append(row)
            continue
        texts = {}
        failed = False
        try:
            if type(row.get('kinopoiskId')) is int:
                found_id, found_score, texts = details(row, token, get)
            else:
                found_id, found_score = lookup(row, token, get)
        except QuotaReached as error:
            stopped = error.args[0]
            result.append(row)
            continue
        except Exception as error:
            # A failed attempt moves behind untouched titles; its old score stays.
            failures.append(f'{type(error).__name__}: {error}')
            found_id, found_score = row.get('kinopoiskId'), None
            failed = True
        done += 1
        fresh = dict(row, kinopoiskCheckedAtUtc=now.isoformat().replace('+00:00', 'Z'))
        if found_id:
            fresh['kinopoiskId'] = found_id
        if found_score:
            fresh['kinopoisk'] = found_score
        if type(row.get('kinopoiskId')) is int and found_id == row['kinopoiskId'] and not failed:
            fresh.update(texts)
            fresh['kinopoiskTextAtUtc'] = fresh['kinopoiskCheckedAtUtc']
        result.append(fresh)
        time.sleep(pause)
    if stopped:
        print('Kinopoisk stopped by HTTP', stopped, 'after', done, 'lookups; previous scores preserved', flush=True)
    if failures:
        print('Kinopoisk lookups failed:', len(failures), 'first:', failures[0][:200], flush=True)
    print('Kinopoisk:', done, 'lookups;', sum(bool(x.get('kinopoisk')) for x in result), 'scores', flush=True)
    return result, done
