"""Read public endpoints; report only bounded source metadata, never cookies."""
import concurrent.futures
import json
import re
import urllib.parse
import urllib.request


def probe(pair):
    label, url = pair
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'Kachalka/0.28'}), timeout=15) as response:
            raw = response.read(2_000_001)
            if len(raw) > 2_000_000:
                raise ValueError('Oversized response')
            result = {'source': label, 'status': response.status, 'url': response.url}
        try:
            data = json.loads(raw)
            rows = data if isinstance(data, list) else data.get('data', data.get('list', []))
            result.update(count=len(rows), rows=rows[:2])
        except (ValueError, AttributeError):
            text = raw.decode('cp1251' if label.startswith('BigFanGroup') else 'utf-8', errors='replace')
            result.update(title=re.findall('<title[^>]*>(.*?)</title>', text, re.S)[:1], links=re.findall(r'href=["\']([^"\']*(?:magnet:|torrent|download|viewtopic|details)[^"\']*)', text)[:15], snippets=re.findall(r'.{0,100}(?:class="ttable|magnet:|Экран|Наруто|Интерстеллар).{0,1800}', text)[:2])
        return result
    except Exception as error:
        return {'source': label, 'error': str(error)}


if __name__ == '__main__':
    q = urllib.parse.quote
    jobs = [
        ('BigFanGroup film', 'https://bigfangroup.org/browse.php?ajax=1&search='+urllib.parse.quote_from_bytes('Интерстеллар'.encode('cp1251'))+'&cat=0&incldead=1&year=0&format=0&s=seed&d=desc'),
        ('BigFanGroup series', 'https://bigfangroup.org/browse.php?ajax=1&search='+urllib.parse.quote_from_bytes('Южный парк'.encode('cp1251'))+'&cat=0&incldead=1&year=0&format=0&s=seed&d=desc'),
        ('AniLiberty search', 'https://aniliberty.top/api/v1/app/search/releases?search='+q('Наруто')),
        ('AniLiberty release', 'https://aniliberty.top/api/v1/anime/releases/naruto'),
        ('AniLibria release', 'https://anilibria.top/api/v1/anime/releases/naruto'),
    ]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(probe, jobs):
            print(json.dumps(result, ensure_ascii=False), flush=True)
