"""Read-only public source diagnostics: collect bounded metadata and search HTML."""
import concurrent.futures
import html
import json
import re
import urllib.request
import urllib.parse


def probe(label, url, encoding='utf-8'):
    try:
        request = urllib.request.Request(url, headers={'User-Agent': 'Kachalka/0.11'})
        with urllib.request.urlopen(request, timeout=18) as response:
            raw = response.read(2_000_001)
            if len(raw) > 2_000_000:
                raise ValueError('oversized')
            final, status = response.url, response.status
        text = raw.decode(encoding, errors='replace')
        # Log public metadata and markup relevant to the parser, never cookies or auth.
        snippets = re.findall(r'.{0,80}(?:alternativeHeadline|entity-rating|ratingValue|topictitle|class="nam"|magnet:|imdb\.com|kinopoisk\.ru|results-item-rating).{0,280}', text, re.I)
        titles = re.findall(r'<a[^>]+href=["\'][^"\']*(?:torrent/|viewtopic\.php|details\.php)[^"\']*["\'][^>]*>(.*?)</a>', text, re.I | re.S)
        rows = re.findall(r'<tr\b[^>]*>([\s\S]*?)</tr>', text, re.I)
        matching_rows = [re.sub(r'\s+', ' ', row)[:6500] for row in rows if 'До последнего грамма' in html.unescape(row) or 'Объект преступления' in html.unescape(row)]
        ratings = re.findall(r'.{0,100}(?:rating|imdb|kinopoisk|Кинопоиск|IMDb).{0,500}', text, re.I)
        result = {'source': label, 'status': status, 'url': final,
                  'title': re.findall(r'<title[^>]*>(.*?)</title>', text, re.I | re.S)[:1],
                  'names': [html.unescape(re.sub('<[^>]+>', '', value)).strip()[:240] for value in titles[:10]],
                  'snippets': snippets[:4], 'rows': matching_rows[:2], 'ratings': ratings[:10] if 'detail' in label else [],
                  'json': text[:5000] if 'suggestion' in label else None,
                  'forms': [{'action':re.findall(r'action=["\']([^"\']+)', form[:400],re.I), 'fields':re.findall(r'\bname=["\']([^"\']+)', form,re.I)} for form in re.findall(r'<form\b[\s\S]*?</form>',text,re.I)[:3]] if label=='BitRu home' else []}
    except Exception as error:
        result = {'source': label, 'error': str(error)}
    return result


if __name__ == '__main__':
    terms = [('Объект преступления', 'obekt-prestupleniya'), ('До последнего грамма', 'do-poslednego-gramma')]
    jobs = []
    for title, slug in terms:
        query = urllib.parse.quote(title)
        cp = urllib.parse.quote_from_bytes(title.encode('cp1251'))
        jobs.extend([
            (title+' detail', 'https://w6.zona.plus/movies/'+slug),
            (title+' rutor.info', 'https://rutor.info/search/0/0/000/0/'+query),
            (title+' rutor.is', 'https://rutor.is/search/0/0/000/0/'+query),
            (title+' nnm utf8', 'https://nnmclub.to/forum/tracker.php?nm='+query, 'cp1251'),
            (title+' nnm cp1251', 'https://nnmclub.to/forum/tracker.php?nm='+cp, 'cp1251'),
            (title+' megapeer', 'https://megapeer.vip/browse.php?search='+cp+'&stype=0', 'cp1251'),
            (title+' kinozal', 'https://kinozal.tv/browse.php?s='+cp, 'cp1251'),
            (title+' bitru', 'https://bitru.org/search.php?search='+query),
        ])
    for title, query in [('The Weight','the weight'), ('Object','l objet du delit')]:
        jobs.append((title+' imdb suggestion', 'https://v3.sg.media-imdb.com/suggestion/x/'+urllib.parse.quote(query)+'.json'))
    for title, _ in terms:
        jobs.append((title+' kinopoisk suggestion', 'https://www.kinopoisk.ru/api/suggest/v2/?query='+urllib.parse.quote(title)))
    jobs.extend([
        ('BitRu home', 'https://bitru.org/'),
        ('BitRu The Weight search', 'https://bitru.org/browse.php?s='+urllib.parse.quote('До последнего грамма')),
        ('BitRu Object search', 'https://bitru.org/browse.php?s='+urllib.parse.quote('Объект преступления')),
        ('Rustorka The Weight', 'https://rustorka.com/forum/tracker.php?nm='+urllib.parse.quote('До последнего грамма'), 'cp1251'),
        ('TorLook The Weight', 'https://torlook.info/?q='+urllib.parse.quote('До последнего грамма')),
        ('The Weight imdb detail', 'https://www.imdb.com/title/tt10794054/'),
        ('Object imdb detail', 'https://www.imdb.com/title/tt37535559/'),
        ('The Weight kinopoisk search', 'https://www.kinopoisk.ru/s/type/film/find/'+urllib.parse.quote('До последнего грамма')+'/'),
    ])
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(lambda args: probe(*args), jobs):
            print(json.dumps(result, ensure_ascii=False), flush=True)
    try:
        import gzip
        with urllib.request.urlopen('https://datasets.imdbws.com/title.ratings.tsv.gz',timeout=30) as response:
            with gzip.GzipFile(fileobj=response) as stream:
                found=[]
                for line in stream:
                    if line.startswith((b'tt10794054\t',b'tt37535559\t')):found.append(line.decode().strip())
        print(json.dumps({'source':'Official IMDb ratings dataset','ratings':found},ensure_ascii=False),flush=True)
    except Exception as error:print(json.dumps({'source':'Official IMDb ratings dataset','error':str(error)}),flush=True)
