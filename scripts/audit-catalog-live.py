"""Compare bounded live search samples with their real source; never download media."""
import argparse
import concurrent.futures
import datetime
import hashlib
import importlib.util
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('refresh_library', ROOT / 'refresh-library.py')
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)
BASELINES = ['гарри поттер', 'властелин колец', 'матрица', 'терминатор', 'чужой',
             'назад в будущее', 'крёстный отец', 'индиана джонс', 'звёздные войны',
             'пираты карибского моря', 'один дома', 'миссия невыполнима']


def read(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'KachalkaSearchAudit/1.0 Mozilla/5.0'})
    with urllib.request.urlopen(request, timeout=25) as response:
        data = response.read(4_000_001)
        if len(data) > 4_000_000:
            raise ValueError('Oversized search response')
        return data


def probe(query, site):
    result = {'query': query, 'section': 'movies'}
    try:
        raw = read('https://w6.zona.plus/search-form?' + urllib.parse.urlencode({'query': query}))
        source = collector.parse_cards(raw.decode('utf-8'), 'movies', 'movies')
        if not source:
            result.update(status='unverified', reason='source returned no identifiable movies')
            return result
        data = json.loads(read(site.rstrip('/') + '/api/catalog?' + urllib.parse.urlencode({'section': 'movies', 'q': query})))
        if not isinstance(data.get('items'), list):
            raise ValueError('Invalid catalog response')
        published = {item['id'] for item in data['items'] if item.get('section') == 'movies'}
        missing = [{key: row[key] for key in ['id', 'title', 'year', 'pageUrl']} for row in source if row['id'] not in published]
        status='window-limited' if missing and len(published)>=80 else 'incomplete' if missing else 'complete'
        result.update(status=status, sourceCount=len(source), siteCount=len(published), missing=missing)
    except (urllib.error.URLError, TimeoutError, ValueError, KeyError) as error:
        result.update(status='unverified', reason=type(error).__name__)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--site', default='https://ka4alka-online-new.peregon.chatgpt.site')
    parser.add_argument('--catalog', type=Path)
    parser.add_argument('--report', type=Path, default=Path('test-output/live-search-audit.json'))
    parser.add_argument('--limit', type=int, default=24)
    parser.add_argument('--offset', type=int)
    parser.add_argument('--strict', action='store_true', help='Fail on incomplete or unverified samples')
    args = parser.parse_args()
    if not 1 <= args.limit <= 100:
        parser.error('--limit must be 1..100')
    queries = list(BASELINES)
    catalog_hash = None
    if args.catalog:
        raw = args.catalog.read_bytes();catalog_hash = hashlib.sha256(raw).hexdigest()
        data = json.loads(raw);rows = data if isinstance(data, list) else data['items']
        groups = {}
        for row in rows:
            words = row.get('title', '').lower().split()
            if row.get('section') == 'movies' and len(words) >= 2 and all(len(word) >= 3 for word in words[:2]):
                prefix = ' '.join(words[:2]);groups.setdefault(prefix, set()).add(row['id'])
        candidates = sorted(prefix for prefix, ids in groups.items() if len(ids) >= 2 and prefix not in queries)
        if candidates:
            start = (args.offset if args.offset is not None else datetime.date.today().toordinal()*max(1,args.limit-len(BASELINES))) % len(candidates)
            queries += candidates[start:] + candidates[:start]
    queries = list(dict.fromkeys(queries))[:args.limit]
    started = time.monotonic();results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        for result in executor.map(lambda query: probe(query, args.site), queries):
            results.append(result)
            print(result['query'], result['status'], 'missing', len(result.get('missing', [])), flush=True)
    report = dict(schemaVersion=1, generatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  scope='bounded-live-sample', site=args.site, catalogSha256=catalog_hash,
                  maxConcurrentQueries=2, queries=len(results), elapsedSeconds=round(time.monotonic()-started, 2), results=results)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.strict and any(row['status'] not in ['complete', 'window-limited'] for row in results):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
