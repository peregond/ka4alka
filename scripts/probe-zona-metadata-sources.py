"""Read-only evidence about metadata exposed by two actual public Zona films.

This diagnostic does not infer a private upstream provider from a rating label
or a CDN filename. It saves selected public facts, never headers or raw HTML.
"""
import concurrent.futures
import html
import json
import pathlib
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from html.parser import HTMLParser


MAXIMUM_BYTES = 4 * 1024 * 1024
OUTPUT = pathlib.Path('test-output/zona-metadata-source')
FILMS = [
    ('Курьер / Runner (2026)', 'https://w6.zona.plus/movies/kurer-2026'),
    ('До последнего грамма / The Weight (2026)', 'https://w6.zona.plus/movies/do-poslednego-gramma'),
]
VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr'}


def clean(text, limit=200):
    return re.sub(r'\s+', ' ', html.unescape(text)).strip()[:limit]


def public_url(value, base):
    """Drop credentials, query values and fragments from every logged URL."""
    try:
        uri = urllib.parse.urlsplit(urllib.parse.urljoin(base, html.unescape(value)))
        if uri.scheme not in ['http', 'https'] or not uri.hostname or uri.username or uri.password:
            return None
        port = uri.port
        if port not in [None, 80, 443]:
            return None
        return urllib.parse.urlunsplit((uri.scheme, uri.hostname, uri.path, '', ''))
    except ValueError:
        return None


class Redirects(urllib.request.HTTPRedirectHandler):
    max_redirections = 5

    def __init__(self):
        super().__init__()
        self.hops = []

    def redirect_request(self, request, response, code, message, headers, newurl):
        target = urllib.parse.urlsplit(newurl)
        host = target.hostname or ''
        official = any(host == domain or host.endswith('.' + domain)
                       for domain in ['zona.plus', 'zona.pub', 'zona.ru', 'zona.mobi', 'zona.video'])
        if target.scheme != 'https' or target.port not in [None, 443] or target.username or not official:
            raise ValueError('Redirect outside verified public Zona domains: ' + str(public_url(newurl, request.full_url)))
        self.hops.append({'status': code, 'from': public_url(request.full_url, request.full_url),
                          'to': public_url(newurl, request.full_url)})
        return super().redirect_request(request, response, code, message, headers, newurl)


class Markup(HTMLParser):
    def __init__(self, base):
        super().__init__(convert_charrefs=True)
        self.base = base
        self.stack, self.links, self.images, self.scripts, self.structured = [], [], [], [], []
        self.ratings, self.footer, self.title = {}, [], []
        self.description_characters = 0

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        parent_footer = any(frame['footer'] for frame in self.stack)
        frame = {'tag': tag, 'attrs': attrs, 'text': [],
                 'footer': parent_footer or tag == 'footer' or 'footer' in attrs.get('class', '').lower()}
        if tag == 'a' and attrs.get('href'):
            frame['link'] = {'url': public_url(attrs['href'], self.base), 'itemprop': attrs.get('itemprop', ''), 'text': ''}
            self.links.append(frame['link'])
        if tag == 'script' and attrs.get('src'):
            self.scripts.append(public_url(attrs['src'], self.base))
        if tag == 'img' or tag == 'meta' and attrs.get('itemprop') == 'image':
            uri = public_url(attrs.get('src') or attrs.get('data-src') or attrs.get('content', ''), self.base)
            if uri:
                self.images.append({'url': uri, 'alt': clean(attrs.get('alt', ''), 100)})
        if tag not in VOID:
            self.stack.append(frame)

    def handle_endtag(self, tag):
        index = next((index for index in range(len(self.stack)-1, -1, -1) if self.stack[index]['tag'] == tag), None)
        if index is None:
            return
        frames, self.stack = self.stack[index:], self.stack[:index]
        for frame in frames:
            text = ''.join(frame['text'])
            if 'link' in frame:
                frame['link']['text'] = clean(text, 100)
            if frame['tag'] == 'script' and frame['attrs'].get('type') == 'application/ld+json':
                try:
                    self.structured.append(json.loads(text))
                except json.JSONDecodeError:
                    pass
            for rating in ['entity-rating-kp', 'entity-rating-imdb']:
                if rating in frame['attrs'].get('class', '').split():
                    self.ratings[rating] = clean(text, 30)

    def handle_data(self, data):
        for frame in self.stack:
            if frame['tag'] in ['a', 'script'] or 'entity-rating-' in frame['attrs'].get('class', ''):
                frame['text'].append(data)
        if any(frame['tag'] == 'title' for frame in self.stack):
            self.title.append(data)
        if any(frame['attrs'].get('itemprop') == 'description' for frame in self.stack):
            self.description_characters += len(data.strip())
        if any(frame['footer'] for frame in self.stack) and not any(frame['tag'] in ['script', 'style'] for frame in self.stack):
            self.footer.append(data)


def schema_facts(node, base):
    facts = []
    if isinstance(node, list):
        return [fact for child in node for fact in schema_facts(child, base)][:20]
    if not isinstance(node, dict):
        return facts
    if '@graph' in node:
        facts.extend(schema_facts(node['@graph'], base))
    types = node.get('@type', [])
    types = [types] if isinstance(types, str) else types
    if isinstance(types, list) and any(value in ['Movie', 'TVSeries', 'Person', 'Organization'] for value in types):
        selected = {'type': types, 'name': clean(str(node.get('name', '')), 150)}
        for key in ['url', '@id', 'sameAs']:
            values = node.get(key, [])
            values = values if isinstance(values, list) else [values]
            selected[key] = [uri for value in values[:10] if isinstance(value, str) and (uri := public_url(value, base))]
        rating = node.get('aggregateRating')
        if isinstance(rating, dict):
            selected['aggregateRating'] = {key: clean(str(rating[key]), 40) for key in ['ratingValue', 'ratingCount', 'reviewCount', 'bestRating', 'worstRating'] if key in rating}
        facts.append(selected)
    return facts[:20]


def evidence(label, requested, final, text, status, redirects):
    parser = Markup(final)
    parser.feed(text)
    links = [link for link in parser.links if link['url']]
    own_host = urllib.parse.urlsplit(final).hostname
    external = sorted({urllib.parse.urlsplit(link['url']).hostname for link in links if urllib.parse.urlsplit(link['url']).hostname != own_host})
    provider_links = [link for link in links if re.search(r'kinopoisk|imdb|themoviedb|tmdb|kino-teatr|кинопоиск|источник|source|данные|powered', link['url'] + ' ' + link['text'], re.I)]
    about_links = [link for link in links if re.search(r'about|copyright|terms|privacy|agreement|faq|contact|о проекте|правооблад|соглашен|источник', link['url'] + ' ' + link['text'], re.I)]
    credit_links = [link for link in links if link['itemprop'] in ['actor', 'director', 'cinematographer'] or re.search(r'/persons?/|/people/', link['url'])]
    identifiers = {
        'imdbIds': list(dict.fromkeys(re.findall(r'\btt\d{6,12}\b', text)))[:12],
        'explicitKinopoiskIds': list(dict.fromkeys(re.findall(r'kinopoisk\.ru/(?:film|series)/(\d{1,10})', text)))[:12],
        'numericPosterFilenames': list(dict.fromkeys(re.findall(r'/images/film_\d+/\d+/(\d+)\.(?:jpg|png)', text)))[:12],
    }
    return {'film': label, 'requestedUrl': requested, 'finalUrl': public_url(final, requested), 'status': status,
            'redirects': redirects, 'title': clean(''.join(parser.title), 160), 'bytes': len(text.encode('utf-8')),
            'descriptionCharacters': parser.description_characters, 'ratings': parser.ratings, 'identifiers': identifiers,
            'externalLinkHosts': external[:30], 'sourceLinks': provider_links[:12], 'aboutLinks': about_links[:12],
            'creditLinks': credit_links[:12], 'images': parser.images[:8], 'scriptUrls': list(dict.fromkeys(uri for uri in parser.scripts if uri))[:10],
            'structuredMetadata': [fact for node in parser.structured for fact in schema_facts(node, final)][:16],
            'footer': clean(' '.join(parser.footer), 800),
            'interpretation': 'Public labels and mirrored image filenames alone do not prove the private upstream provider.'}


def json_evidence(label, requested, final, data, status, redirects):
    """Whitelist public metadata fields from the historical keyless JSON route."""
    result = {'film': label, 'requestedUrl': requested, 'finalUrl': public_url(final, requested),
              'status': status, 'redirects': redirects, 'returnedFormat': 'JSON'}
    if not isinstance(data, dict):
        result['error'] = 'JSON is not a metadata object'
        return result
    result['rootFields'] = sorted(data)[:40]
    movie = data.get('movie', data.get('serial'))
    if isinstance(movie, dict):
        result['movieFields'] = sorted(movie)[:50]
        result['movie'] = {key: clean(str(movie[key]), 180) for key in
                           ['id', 'name_rus', 'name_original', 'name_eng', 'year', 'rating', 'rating_kinopoisk', 'rating_imdb', 'kinopoisk_id', 'imdb_id']
                           if key in movie and isinstance(movie[key], (str, int, float))}
        result['descriptionCharacters'] = len(str(movie.get('description', '')))
        result['images'] = [{key: uri} for key in ['image', 'cover']
                            if isinstance(movie.get(key), str) and (uri := public_url(movie[key], final))]
    persons = data.get('persons')
    if isinstance(persons, dict):
        result['personRoles'] = sorted(persons)[:20]
        selected = {}
        for role in ['actors', 'actor', 'director', 'directors', 'cinematographer', 'cinematographers', 'operator', 'operators', 'scenarist']:
            people = persons.get(role)
            if not isinstance(people, list):
                continue
            selected[role] = []
            for person in people[:5]:
                if not isinstance(person, dict):
                    continue
                facts = {'fields': sorted(person)[:25]}
                for key in ['name', 'name_rus', 'name_original', 'name_eng']:
                    if isinstance(person.get(key), str):
                        facts[key] = clean(person[key], 150)
                for key in ['id', 'kinopoisk_id', 'imdb_id']:
                    if isinstance(person.get(key), (str, int)) and re.fullmatch(r'(?:nm)?\d{1,12}', str(person[key])):
                        facts[key] = str(person[key])
                for key in ['cover', 'image', 'url', 'page', 'link']:
                    if isinstance(person.get(key), str) and (uri := public_url(person[key], final)):
                        facts[key] = uri
                for key in ['description', 'biography']:
                    if isinstance(person.get(key), str):
                        facts[key + 'Characters'] = len(person[key])
                selected[role].append(facts)
        result['people'] = selected
    result['interpretation'] = 'Public Zona JSON metadata does not itself disclose the upstream provider or a documented external API.'
    return result


def probe(film):
    label, url, mode = film
    redirects = Redirects()
    try:
        headers = {'User-Agent': 'Kachalka/0.34'}
        if mode == 'JSON/XMLHttpRequest':
            # Public open-source Kodi client uses these ordinary content-
            # negotiation headers on the same detail URL; no key or login.
            headers.update({'Accept': 'application/json, text/javascript, */*; q=0.01',
                            'X-Requested-With': 'XMLHttpRequest'})
        request = urllib.request.Request(url, headers=headers)
        deadline = time.monotonic() + 25
        with urllib.request.build_opener(redirects).open(request, timeout=12) as response:
            chunks, size = [], 0
            while True:
                if time.monotonic() > deadline:
                    raise TimeoutError('Public page exceeded the diagnostic deadline')
                chunk = response.read1(64 * 1024)
                if not chunk:
                    break
                size += len(chunk)
                if size > MAXIMUM_BYTES:
                    raise ValueError('Public page exceeded the size limit')
                chunks.append(chunk)
            raw = b''.join(chunks)
            text = raw.decode(response.headers.get_content_charset() or 'utf-8', errors='replace')
            if text.lstrip().startswith(('{', '[')):
                result = json_evidence(label, url, response.url, json.loads(text), response.status, redirects.hops)
            else:
                result = evidence(label, url, response.url, text, response.status, redirects.hops)
                result['returnedFormat'] = 'HTML'
            result.update(requestedRepresentation=mode, responseContentType=clean(response.headers.get('Content-Type', ''), 100))
            return result
    except Exception as error:
        result = {'film': label, 'requestedUrl': url, 'redirects': redirects.hops,
                  'requestedRepresentation': mode, 'errorType': type(error).__name__, 'error': clean(str(error), 300)}
        if isinstance(error, urllib.error.HTTPError):
            result.update(status=error.code, finalUrl=public_url(error.url, url))
        return result


if __name__ == '__main__':
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(probe, [(label, url, mode) for label, url in FILMS for mode in ['HTML', 'JSON/XMLHttpRequest']]))
    for result in results:
        print(json.dumps(result, ensure_ascii=False), flush=True)
    (OUTPUT / 'evidence.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print('DIAGNOSTIC ONLY: source availability is reported honestly; no live validation PASS is claimed.')
