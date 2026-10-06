"""Refresh the real catalog snapshot, with bounded sequential requests and no invented entries."""
import html, json, re, time, urllib.request
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
items = {}
for section, path, pages in [('movies', 'movies', 60), ('series', 'tvseries', 10)]:
    for page in range(1, pages + 1):
        url = f'https://w6.zona.plus/{path}/filter/sort-date?page={page}'
        for attempt in range(3):
            try:
                request = urllib.request.Request(url, headers={'User-Agent': 'KachalkaCatalog/0.21', 'Accept': 'text/html'})
                with urllib.request.urlopen(request, timeout=30) as response:
                    data = response.read(4_000_001).decode('utf-8')
                if len(data) > 4_000_000: raise ValueError('Oversized catalog')
                cards = re.findall(r'<li\s+class=[\"\']results-item-wrap[\"\'][^>]*>[\s\S]*?</li>', data, re.I)
                if not cards: raise ValueError('No catalog cards')
                break
            except Exception:
                if attempt == 2: raise
                time.sleep(2)
        if page in [1, 2, 50, 60]:
            print(section, page, 'pagination:', re.findall(r'<[^>]+href=[\"\'][^\"\']*[?&]page=\d+[^>]*>', data)[-12:], flush=True)
        count = 0
        for card in cards:
            def attr(tag, name):
                match = re.search(name + r'=[\"\']([^\"\']+)[\"\']', tag, re.I)
                return html.unescape(match[1]) if match else None
            anchor = re.search(r'<a\b[^>]*itemprop=[\"\']url[\"\'][^>]*>', card, re.I)
            href = attr(anchor[0], 'href') if anchor else None
            title = re.search(r'<[^>]+itemprop=[\"\']name[\"\'][^>]*>([\s\S]*?)</', card, re.I)
            if not href or not href.startswith('/' + path + '/') or not title: continue
            title = html.unescape(re.sub('<[^>]*>', '', title[1])).strip()
            year = re.search(r'class=[\"\']results-item-year[\"\'][^>]*>(\d{4})', card, re.I)
            poster_tag = re.search(r'<meta\b[^>]*itemprop=[\"\']image[\"\'][^>]*>', card, re.I)
            slug = href.split('/')[2]
            item = dict(id=f'{section}:{slug}', section=section, title=title, year=int(year[1]) if year else 0,
                        poster=attr(poster_tag[0], 'content') if poster_tag else None, pageUrl='https://w6.zona.plus' + href)
            items[item['id']] = item
            count += 1
        print(section, 'page', page, 'cards', count, 'unique total', len(items), flush=True)
        time.sleep(.25)
assert sum(x['section'] == 'movies' for x in items.values()) >= 2000, 'Need at least 2000 distinct real movies'
(ROOT / 'web-index/app/data/seed.json').write_text(json.dumps(list(items.values()), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('Saved', len(items), 'real entries', flush=True)
