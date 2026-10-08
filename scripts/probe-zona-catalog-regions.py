"""Read-only evidence for country metadata in public Zona catalog lists."""
import concurrent.futures
import importlib.util
import json
import pathlib
import re
import time
import urllib.error
import urllib.request
helper_spec = importlib.util.spec_from_file_location('zona_metadata_helpers', pathlib.Path(__file__).with_name('probe-zona-metadata-sources.py'))
helper = importlib.util.module_from_spec(helper_spec)
helper_spec.loader.exec_module(helper)
Redirects, clean, public_url = helper.Redirects, helper.clean, helper.public_url


OUTPUT = pathlib.Path('test-output/zona-catalog-regions')
URLS = [
    'https://w6.zona.plus/movies/filter/sort-date?page=1',
    'https://w6.zona.plus/tvseries/filter/sort-date?page=1',
    'https://w6.zona.plus/movies/filter/country-rossiia/sort-date?page=1',
    'https://w6.zona.plus/movies/filter/country-ssha/sort-date?page=1',
]


def shape(value, depth=0):
    if isinstance(value, dict):
        if depth >= 2:
            return {'type': 'object', 'fields': sorted(value)[:45]}
        result = {'type': 'object', 'fields': sorted(value)[:45]}
        selected = ['id', 'name_id', 'name', 'name_rus', 'name_original', 'year', 'serial',
                    'country_id', 'country', 'countryIds', 'translit', 'genreId', 'page', 'pages', 'total', 'count']
        result['facts'] = {key: clean(str(value[key]), 160) for key in selected if key in value and isinstance(value[key], (str, int, float, bool))}
        result['children'] = {key: shape(child, depth+1) for key, child in value.items()
                              if isinstance(child, (list, dict)) and not re.search('person|actor|director|user|cookie|token|session', key, re.I)}
        return result
    if isinstance(value, list):
        return {'type': 'array', 'length': len(value), 'items': [shape(child, depth) for child in value[:6]]}
    return {'type': type(value).__name__}


def probe(work):
    url, mode = work
    redirects = Redirects()
    result = {'url': url, 'representation': mode}
    try:
        headers = {'User-Agent': 'Kachalka/0.35'}
        if mode == 'JSON':
            headers.update({'Accept': 'application/json, text/javascript, */*; q=0.01', 'X-Requested-With': 'XMLHttpRequest'})
        deadline = time.monotonic()+20
        with urllib.request.build_opener(redirects).open(urllib.request.Request(url, headers=headers), timeout=12) as response:
            chunks, size = [], 0
            while True:
                if time.monotonic() > deadline:
                    raise TimeoutError('Public catalog deadline exceeded')
                chunk = response.read1(64*1024)
                if not chunk:
                    break
                size += len(chunk)
                if size > 4*1024*1024:
                    raise ValueError('Public catalog size limit exceeded')
                chunks.append(chunk)
            text = b''.join(chunks).decode(response.headers.get_content_charset() or 'utf-8', errors='replace')
            result.update(status=response.status, finalUrl=public_url(response.url, url), bytes=size,
                          contentType=clean(response.headers.get('Content-Type', ''), 100), redirects=redirects.hops)
            if text.lstrip().startswith(('{', '[')):
                result['returnedFormat'] = 'JSON'
                result['shape'] = shape(json.loads(text))
            else:
                result['returnedFormat'] = 'HTML'
                result['selectedCountryOptions'] = [clean(match.group(0), 180) for match in re.finditer(r'<option[^>]*(?:country-|selected)[^>]*>[^<]*', text, re.I) if 'country-' in match.group(0) and 'selected' in match.group(0)][:10]
                result['countryMarkup'] = [clean(text[max(0, match.start()-70):match.end()+120], 250) for match in list(re.finditer(r'country_id|countryIds|data-country|countryOfOrigin|itemprop=[\"\x27]country', text, re.I))[:12]]
                result['cardCount'] = len(re.findall('results-item-wrap', text))
    except Exception as error:
        result.update(errorType=type(error).__name__, error=clean(str(error), 240))
        if isinstance(error, urllib.error.HTTPError):
            result['status'] = error.code
    return result


if __name__ == '__main__':
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(probe, [(url, mode) for url in URLS for mode in ['HTML', 'JSON']]))
    (OUTPUT/'evidence.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    for result in results:
        print(json.dumps(result, ensure_ascii=False), flush=True)
