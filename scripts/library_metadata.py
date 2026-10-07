"""Bounded metadata enrichment; IMDb scores come from its official public dataset."""
import concurrent.futures
import gzip
import html
import io
import json
import re
import unicodedata
import urllib.parse
import urllib.request

FEED = 'https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog.json'
RATINGS = 'https://datasets.imdbws.com/title.ratings.tsv.gz'
# IMDb uses the international title Crescendo for this verified French title.
ALIASES = {('l objet du delit', 2026): 'tt37535559'}


def normalize(value):
    value = ''.join(c for c in unicodedata.normalize('NFKD', value.casefold()) if not unicodedata.combining(c))
    return re.sub(r'[^\w]+', ' ', value).strip()


def read(url, maximum=4_000_000):
    request = urllib.request.Request(url, headers={'User-Agent': 'KachalkaCatalog/0.27'})
    with urllib.request.urlopen(request, timeout=12) as response:
        data = response.read(maximum+1)
    if len(data) > maximum:
        raise ValueError('Oversized metadata')
    return data


def parse_detail(text):
    def meta(name):
        for tag in re.findall(r'<meta\b[^>]*>', text, re.I):
            if re.search(r'itemprop=["\']'+name+r'["\']', tag, re.I):
                match = re.search(r'content=(["\'])(.*?)\1', tag, re.I)
                if match:
                    return html.unescape(match[2]).strip()
        return None
    result = {}
    original = meta('alternativeHeadline')
    if original:
        result['originalTitle'] = original[:200]
    for cls, key in [('entity-rating-kp', 'kinopoisk'), ('entity-rating-imdb', 'imdb')]:
        found = re.search(r'class=["\'][^"\']*\b'+cls+r'\b[^"\']*["\'][^>]*>([\s\S]*?)</', text, re.I)
        value = html.unescape(re.sub('<[^>]*>', '', found[1])).strip() if found else ''
        if re.fullmatch(r'\d{1,2}(?:[.,]\d)?', value) and 0 < float(value.replace(',', '.')) <= 10:
            result[key] = value
    return result


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


def enrich_one(item):
    row = dict(item)
    try:
        row.update(parse_detail(read(row['pageUrl']).decode('utf-8')))
    except Exception:
        return row
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


def enrich(rows):
    # Reuse confirmed identities for older cards instead of recrawling the whole library.
    try:
        previous = json.loads(read(FEED))
        old = {x['id']: x for x in previous.get('items', [])}
        for row in rows:
            prior = old.get(row['id'], {})
            if prior.get('year') == row['year'] and prior.get('pageUrl') == row['pageUrl']:
                for key in ['originalTitle', 'kinopoisk', 'imdb', 'imdbId']:
                    if prior.get(key):
                        row[key] = prior[key]
    except Exception:
        pass
    selected = [r for section, count in [('movies', 100), ('series', 20)] for r in [x for x in rows if x['section'] == section][:count]]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        updates = {x['id']: x for x in pool.map(enrich_one, selected)}
    rows = [updates.get(x['id'], x) for x in rows]
    ids = {x['imdbId'] for x in rows if re.fullmatch(r'tt\d{6,12}', x.get('imdbId', ''))}
    if ids:
        try:
            scores = official_scores(read(RATINGS, 20_000_000), ids)
            for row in rows:
                if row.get('imdbId') in scores:
                    row['imdb'] = scores[row['imdbId']]
        except Exception as error:
            print('IMDb refresh unavailable; confirmed previous scores preserved:', type(error).__name__, flush=True)
    print('Metadata:', sum(bool(x.get('originalTitle')) for x in rows), 'original titles;', sum(bool(x.get('imdb')) for x in rows), 'IMDb scores', flush=True)
    return rows
