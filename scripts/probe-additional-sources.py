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
            text = raw.decode('utf-8', errors='replace')
            result.update(title=re.findall('<title[^>]*>(.*?)</title>', text, re.S)[:1], links=re.findall(r'href=["\']([^"\']*(?:magnet:|torrent|download|viewtopic|details)[^"\']*)', text)[:15], snippets=re.findall(r'.{0,100}(?:class="ttable|magnet:|Экран|Наруто|Интерстеллар).{0,1800}', text)[:2])
        return result
    except Exception as error:
        return {'source': label, 'error': str(error)}


if __name__ == '__main__':
    q = urllib.parse.quote
    jobs = [
        ('AniLiberty Naruto', 'https://anilibria.top/api/v1/anime/releases?f[search]='+q('Наруто')),
        ('AniLiberty English', 'https://anilibria.top/api/v1/anime/releases?f[search]=Naruto'),
        ('AniLibria v3', 'https://api.anilibria.tv/v3/title/search?search=Naruto&limit=3'),
        ('TorrentBy film', 'https://torrent.by/search/?search='+q('Интерстеллар')+'&category=0'),
        ('TorrentBy series', 'https://torrent.by/search/?search='+q('Южный Парк')+'&category=0'),
        ('Kinozal current mirror', 'https://kinozal.me/browse.php?s='+urllib.parse.quote_from_bytes('Интерстеллар'.encode('cp1251'))),
        ('RiperAM', 'https://riperam.org/forum/tracker.php?nm='+q('Интерстеллар')),
        ('BigFanGroup', 'https://bigfangroup.org/forum/tracker.php?nm='+q('Интерстеллар')),
        ('APIBay Russian film', 'https://apibay.org/q.php?q='+q('Брат')+'&cat=201,202,207'),
        ('APIBay Russian series', 'https://apibay.org/q.php?q=South%20Park%20RUS&cat=205,208'),
    ]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(probe, jobs):
            print(json.dumps(result, ensure_ascii=False), flush=True)
