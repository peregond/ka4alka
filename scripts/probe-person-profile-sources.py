import json
import pathlib
import re
import urllib.request
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
from html.parser import HTMLParser


class Markup(HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms, self.inputs, self.scripts, self.images, self.links = [], [], [], [], []
        self.metadata, self.headings = [], []
        self.active_heading = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'form':
            self.forms.append({k: attrs.get(k) for k in ['action', 'method', 'id']})
        elif tag == 'input':
            self.inputs.append({k: attrs.get(k) for k in ['name', 'type', 'id', 'placeholder']})
        elif tag == 'script' and attrs.get('src'):
            self.scripts.append(attrs['src'])
        elif tag == 'img':
            self.images.append({k: attrs.get(k) for k in ['src', 'alt', 'class']})
        elif tag == 'a' and re.search(r'/person/|/name/|/persons|/search|/actor|page|offset|start=', attrs.get('href', '')):
            self.links.append(attrs.get('href'))
        elif tag == 'meta' and attrs.get('property') in ['og:title', 'og:image', 'og:description']:
            self.metadata.append(attrs)
        if tag in ['h1', 'h2', 'h3']:
            self.active_heading = {'tag': tag, 'attrs': attrs, 'text': ''}
            self.headings.append(self.active_heading)

    def handle_endtag(self, tag):
        if tag in ['h1', 'h2', 'h3']:
            self.active_heading = None

    def handle_data(self, data):
        if self.active_heading is not None:
            self.active_heading['text'] += data.strip()[:160]


def probe(url):
    output = pathlib.Path('test-output/person-profile-source')
    output.mkdir(parents=True, exist_ok=True)
    try:
        request = urllib.request.Request(url, headers={'User-Agent': 'Kachalka/0.34'})
        with urllib.request.urlopen(request, timeout=15) as response:
            data = response.read(4 * 1024 * 1024 + 1)
            if len(data) > 4 * 1024 * 1024:
                raise ValueError('Profile source exceeds size limit')
            encoding = response.headers.get_content_charset() or 'utf-8'
            text = data.decode(encoding, errors='replace')
            parser = Markup()
            parser.feed(text)
            slug = re.sub(r'[^A-Za-z0-9]+', '-', url).strip('-')
            (output / (slug + '.html')).write_text(text, encoding='utf-8')
            all_profiles = list(dict.fromkeys(link for link in parser.links if '/person/' in link))
            evidence = {'url': url, 'finalUrl': response.url, 'status': response.status, 'bytes': len(data),
                        'forms': parser.forms, 'inputs': parser.inputs, 'scripts': parser.scripts,
                        'images': parser.images[:12], 'personLinks': list(dict.fromkeys(parser.links))[:15],
                        'profileLinks': all_profiles[:20], 'profileCount': len(all_profiles),
                        'scottMatches': [link for link in all_profiles if 'waugh-scott-' in link.lower()],
                        'pagination': list(dict.fromkeys(link for link in parser.links if re.search(r'page|offset|start=|/p/|[?&]p=', link)))[:16],
                        'metadata': parser.metadata, 'headings': parser.headings[:8],
                        'structure': [text[max(0, match.start()-180):match.end()+220] for match in
                                      list(re.finditer(r'person_films|biography|itemprop|Waugh|Режисс[её]р|Акт[её]р|Гонка|Жажда скорости', text))[:8]]}
            print(json.dumps(evidence, ensure_ascii=False))
            return evidence
    except Exception as error:
        evidence = {'url': url, 'error': str(error)}
        print(json.dumps(evidence, ensure_ascii=False))
        return evidence


urls = [
            'https://kino-teatr.ua/ru/main/persons/lastname/' + urllib.parse.quote('Во') + '/page/' + str(page) + '.phtml'
            for page in range(2, 10)]
with ThreadPoolExecutor(max_workers=4) as pool:
    results = list(pool.map(probe, urls))
pathlib.Path('test-output/person-profile-source/responses.json').write_text(json.dumps(results, ensure_ascii=False), encoding='utf-8')
print('DIAGNOSTIC ONLY: public page availability is not a verified participant identity.')
