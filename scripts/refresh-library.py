"""Collect a bounded public catalog; publish only complete, validated snapshots."""
import argparse
import datetime
import html
import json
import re
import time
import urllib.request
from pathlib import Path


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
        if not title or not re.fullmatch(r'[\w-]{1,120}', slug):
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
    args = parser.parse_args()
    rows = collect()
    now = datetime.datetime.now(datetime.timezone.utc)
    rows.sort(key=lambda row: min(row['year'], now.year), reverse=True)
    from library_metadata import enrich
    rows = enrich(rows)
    feed = dict(schemaVersion=1, generatedAtUtc=now.isoformat().replace('+00:00', 'Z'),
                source='https://w6.zona.plus', metadataVersion=1, items=rows)
    target = Path(args.output)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp = target.with_suffix('.tmp')
    temp.write_text(json.dumps(feed, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')
    temp.replace(target)
    if args.update_seed:
        data = Path(__file__).resolve().parents[1] / 'web-index/app/data'
        (data / 'seed.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        starter = [x for section in ['movies', 'series'] for x in [r for r in rows if r['section'] == section][:40]]
        (data / 'starter.json').write_text(json.dumps(starter, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Complete catalog:', len(rows), 'entries;', feed['generatedAtUtc'], flush=True)


if __name__ == '__main__':
    main()
