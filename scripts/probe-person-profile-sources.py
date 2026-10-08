import json
import pathlib
import re
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from html.parser import HTMLParser


class Markup(HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms, self.inputs, self.scripts, self.images, self.links = [], [], [], [], []

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
        elif tag == 'a' and re.search(r'/person/|/name/|/persons|/search|/actor', attrs.get('href', '')):
            self.links.append(attrs.get('href'))


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
            evidence = {'url': url, 'finalUrl': response.url, 'status': response.status, 'bytes': len(data),
                        'forms': parser.forms, 'inputs': parser.inputs, 'scripts': parser.scripts,
                        'images': parser.images[:12], 'personLinks': list(dict.fromkeys(parser.links))[:15]}
            print(json.dumps(evidence, ensure_ascii=False))
            return evidence
    except Exception as error:
        evidence = {'url': url, 'error': str(error)}
        print(json.dumps(evidence, ensure_ascii=False))
        return evidence


urls = ['https://kino-teatr.ua/persons.phtml', 'https://kino-teatr.ua/person/Waugh-Scott-6293.phtml',
        'https://www.kinoafisha.info/person/8386748/', 'https://api.tvmaze.com/search/people?q=Scott%20Waugh']
with ThreadPoolExecutor(max_workers=4) as pool:
    results = list(pool.map(probe, urls))
pathlib.Path('test-output/person-profile-source/responses.json').write_text(json.dumps(results, ensure_ascii=False), encoding='utf-8')
print('DIAGNOSTIC ONLY: public page availability is not a verified participant identity.')
