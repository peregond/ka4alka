"""Bounded extraction of textual Zona details, without fetching any assets.

The JSON representation is accepted only after its movie identity agrees with
the catalog card. Ratings and credits are then read from its documented fields,
never from recommendations or an unrelated provider's score.
"""
import html
import json
import re
import unicodedata
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit

MAX_BYTES = 4_000_000
MAX_NODES = 50_000
MAX_DEPTH = 100
BASE = 'https://w6.zona.plus'
ROLES = {'actor': 'Актёры', 'actors': 'Актёры', 'director': 'Режиссёры',
         'operator': 'Операторы', 'cinematographer': 'Операторы'}
PLACEHOLDERS = {'Описание временно недоступно.', 'Загружаем описание…'}


def _scalar(value):
    return str(value) if isinstance(value, (str, int, float)) and not isinstance(value, bool) else ''


def _text(value, maximum=12_000):
    value = _scalar(value)
    # Excluding a nested '<' keeps malformed input from causing quadratic scans.
    value = re.sub(r'<[^<>]*>', ' ', value)
    value = re.sub(r'\s+', ' ', html.unescape(value)).strip()
    return value[:maximum].strip()


def _normalize(value):
    return re.sub(r'[^\w]+', ' ', unicodedata.normalize('NFKC', _text(value)).casefold().replace('ё', 'е')).strip()


def _identifier(value):
    value = _scalar(value)
    return value if re.fullmatch(r'[1-9][0-9]{0,17}', value) else None


def _score(value):
    value = _scalar(value).replace(',', '.').strip()
    if not re.fullmatch(r'[0-9]{1,2}(?:\.[0-9]{1,4})?', value):
        return None
    number = float(value)
    return format(number, '.1f').rstrip('0').rstrip('.') if 0 < number <= 10 and round(number, 1) > 0 else None


def catalog_url(value, *prefixes):
    """Return a canonical URL only for an unambiguous trusted catalog path."""
    if not isinstance(value, str) or not value or len(value) > 1000:
        return None
    value = html.unescape(value).strip()
    if any(c.isspace() or ord(c) < 32 for c in value) or '%' in value or '\\' in value:
        return None
    try:
        parsed = urlsplit(urljoin(BASE, value))
        if (parsed.scheme != 'https' or parsed.hostname != 'w6.zona.plus' or
                parsed.port not in (None, 443) or parsed.username or parsed.password or
                parsed.query or parsed.fragment):
            return None
    except ValueError:
        return None
    if any(re.fullmatch(re.escape(prefix) + r'(?:[^\W_]|-){1,120}/?', parsed.path) for prefix in prefixes):
        return BASE + parsed.path.rstrip('/')
    return None


def photo_url(value, source_person_id=None):
    """Keep only Zona portrait references that agree with the source person ID."""
    if not isinstance(value, str) or len(value) > 300 or '%' in value or '\\' in value:
        return None
    try:
        parsed = urlsplit(value)
        if (parsed.scheme != 'https' or parsed.hostname not in
                {'img1.zonapic.com', 'img2.zonapic.com', 'img3.zonapic.com', 'img4.zonapic.com'} or
                parsed.port not in (None, 443) or parsed.username or parsed.password or
                parsed.query or parsed.fragment):
            return None
    except ValueError:
        return None
    match = re.fullmatch(r'/images/actor/[0-9]+/([0-9]+)\.(?:jpe?g|png|webp)', parsed.path, re.I)
    if not match or source_person_id and match[1] != source_person_id:
        return None
    return 'https://' + parsed.hostname + parsed.path


def _key(value, country=False):
    value = _scalar(value).strip().lower()
    pattern = r'[a-z]+(?:-[a-z]+)*' if country else r'[a-z0-9-]+'
    return value if 0 < len(value) <= (80 if country else 100) and re.fullmatch(pattern, value) else None


def _unique(values, maximum=30):
    return list(dict.fromkeys(value for value in values if value))[:maximum]


def _people(rows):
    seen, result = set(), []
    for row in rows:
        key = (_normalize(row['name']), row['role'], row.get('sourcePersonId'),
               row.get('pageUrl', ''), row.get('profileUrl', ''))
        if key not in seen:
            seen.add(key)
            result.append(row)
        if len(result) == 80:
            break
    return sorted(result, key=lambda row: {'Актёры': 0, 'Режиссёры': 1, 'Операторы': 2}.get(row['role'], 3))


def merge_people(prior, fresh):
    """Merge credits without erasing rich identity fields or conflating namesakes."""
    result = [dict(row) for row in fresh if isinstance(row, dict) and row.get('name') and row.get('role')]
    for old in prior:
        if not isinstance(old, dict) or not old.get('name') or not old.get('role'):
            continue
        matches = [row for row in result if _normalize(row['name']) == _normalize(old['name']) and
                   row['role'] == old['role'] and all(not row.get(key) or not old.get(key) or
                   row[key] == old[key] for key in ('sourcePersonId', 'profileUrl', 'pageUrl'))]
        if len(matches) != 1:
            result.append(dict(old))
            continue
        current = matches[0]
        for key in ('pageUrl', 'profileUrl', 'originalName', 'sourcePersonId'):
            if not current.get(key) and old.get(key):
                current[key] = old[key]
        if not current.get('photoUrl') and old.get('photoUrl'):
            photo = photo_url(old['photoUrl'], current.get('sourcePersonId'))
            if photo:
                current['photoUrl'] = photo
    return _people(result)


def _json_metadata(root, item):
    section = item.get('section')
    name = 'serial' if section == 'series' else 'movie'
    movie = root.get(name)
    if not isinstance(movie, dict):
        return {}
    if 'serial' in movie:
        flag = movie['serial']
        if isinstance(flag, str) and flag.casefold() in ('true', 'false'):
            flag = flag.casefold() == 'true'
        if isinstance(flag, bool) and flag != (section == 'series'):
            return {}
    year_text = _scalar(movie.get('year'))
    if not re.fullmatch(r'[0-9]{4}', year_text) or not 1800 <= int(year_text) <= 2100:
        return {}
    if item.get('year') and int(year_text) != item['year']:
        return {}
    title, original = _text(movie.get('name_rus'), 300), _text(movie.get('name_original'), 300)
    expected = {_normalize(item.get('title')), _normalize(item.get('originalTitle'))} - {''}
    if not expected.intersection({_normalize(title), _normalize(original)} - {''}):
        return {}
    result = {}
    description = _text(movie.get('description'))
    if description and description not in PLACEHOLDERS:
        result['description'] = description
    if original:
        result['originalTitle'] = original
    for field, key in [('rating_kinopoisk', 'kinopoisk'), ('rating_imdb', 'imdb')]:
        score = _score(movie.get(field))
        if score:
            result[key] = score
    for descriptor, selected, label, keys in [('genres', 'genreId', 'genre', 'genreKeys'),
                                             ('countries', 'country_id', 'country', 'countryKeys')]:
        selected_ids = _unique(_scalar(movie.get(selected)).split(), 80)
        selected_set = set(selected_ids)
        rows = root.get(descriptor)
        rows = rows[:2000] if isinstance(rows, list) else []
        choices = [(row.get('id'), _text(row.get('name'), 201), _key(row.get('translit'), label == 'country'))
                   for row in rows if isinstance(row, dict) and _scalar(row.get('id')) in selected_set]
        choices = [choice for choice in choices if 0 < len(choice[1]) <= 200]
        names = _unique(choice[1] for choice in choices)
        chosen_keys = _unique(choice[2] for choice in choices if choice[1])
        if names:
            result[label] = ', '.join(names)
        if chosen_keys:
            result[keys] = chosen_keys
            if label == 'country':
                complete = {_identifier(choice[0]) for choice in choices if choice[1] and choice[2]}
                result['countryKeysComplete'] = bool(selected_ids) and len(choices) <= 30 and all(
                    _identifier(value) and value in complete for value in selected_ids)
    persons = root.get('persons')
    people = []
    if isinstance(persons, dict):
        for key in ('director', 'operator', 'cinematographer', 'actors'):
            rows = persons.get(key)
            if not isinstance(rows, list):
                continue
            for row in rows[:80]:
                if not isinstance(row, dict):
                    continue
                person_name = _text(row.get('name'), 151) or _text(row.get('name_eng'), 151)
                if not 0 < len(person_name) <= 150:
                    continue
                person = {'name': person_name, 'role': ROLES[key], 'pageUrl': ''}
                source_id = _identifier(row.get('i'))
                if source_id:
                    person['sourcePersonId'] = source_id
                original_name = _text(row.get('name_eng'), 151)
                if 0 < len(original_name) <= 150:
                    person['originalName'] = original_name
                photo = photo_url(row.get('cover'), source_id)
                if photo:
                    person['photoUrl'] = photo
                people.append(person)
    if people:
        result['people'] = _people(people)
    return result


class _Node:
    def __init__(self, tag='', attrs=(), parent=None):
        self.tag, self.attrs, self.parent = tag, dict(attrs), parent
        self.children, self.parts = [], []
        self.role_computed, self.local_role = False, None

    def prop(self, name):
        return name in self.attrs.get('itemprop', '').split()

    def text(self):
        parts, remaining = [], 24_000
        for part in self.parts:
            parts.append(part[:remaining])
            remaining -= len(part)
            if remaining <= 0:
                break
        return _text(' '.join(parts))

    def descendants(self):
        pending = [self]
        while pending:
            node = pending.pop()
            yield node
            pending.extend(reversed(node.children))

    def ancestors(self, maximum=MAX_DEPTH):
        node = self
        for _ in range(maximum):
            if node is None:
                break
            yield node
            node = node.parent


class _Document(HTMLParser):
    VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr'}

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = _Node()
        self.stack, self.nodes = [self.root], []

    def handle_starttag(self, tag, attrs):
        if len(self.nodes) >= MAX_NODES or len(self.stack) >= MAX_DEPTH:
            raise ValueError('Oversized HTML structure')
        node = _Node(tag, attrs, self.stack[-1])
        self.stack[-1].children.append(node)
        self.nodes.append(node)
        if tag not in self.VOID:
            self.stack.append(node)

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        for index in range(len(self.stack) - 1, 0, -1):
            if self.stack[index].tag == tag:
                del self.stack[index:]
                break

    def handle_data(self, data):
        if not any(node.tag in ('script', 'style', 'noscript') for node in self.stack):
            # Bound repeated text storage as well as node count and nesting depth.
            for node in self.stack:
                node.parts.append(data)


def _excluded(node):
    return any(parent.tag in ('nav', 'footer', 'script', 'style', 'noscript') or
               set(parent.attrs.get('class', '').split()).intersection(
                   {'results-item-wrap', 'recommendations', 'recommended', 'entity-recommendations'})
               for parent in node.ancestors())


def _role(node):
    for parent in node.ancestors(5):
        if not parent.role_computed:
            parent.role_computed = True
            parent.local_role = next((ROLES[prop] for prop in parent.attrs.get('itemprop', '').split() if prop in ROLES), None)
            if not parent.local_role:
                for child in parent.children:
                    if child.tag in ('dt', 'th') or set(child.attrs.get('class', '').split()).intersection({'label', 'title', 'entity-info-label', 'entity-info-title'}):
                        label = child.text().casefold()
                        if label.startswith(('в ролях', 'актер', 'актёр')):
                            parent.local_role = 'Актёры'
                        elif label.startswith('режисс'):
                            parent.local_role = 'Режиссёры'
                        elif label.startswith('оператор'):
                            parent.local_role = 'Операторы'
                        if parent.local_role:
                            break
        if parent.local_role:
            return parent.local_role
    return None


def parse_html_metadata(text, item=None):
    """Extract schema credits/details while excluding catalog recommendation cards."""
    if not isinstance(text, str) or len(text.encode('utf-8')) > MAX_BYTES:
        return {}
    doc = _Document()
    try:
        doc.feed(text)
        doc.close()
    except (ValueError, RecursionError):
        return {}
    if item:
        expected_url = catalog_url(item.get('pageUrl'), '/movies/', '/tvseries/')
        for node in doc.nodes:
            canonical = (node.tag == 'link' and 'canonical' in node.attrs.get('rel', '').split())
            og_url = node.tag == 'meta' and node.attrs.get('property') == 'og:url'
            if canonical or og_url:
                value = node.attrs.get('href' if canonical else 'content', '')
                if value and catalog_url(value, '/movies/', '/tvseries/') != expected_url:
                    return {}
    scopes = [node for node in doc.nodes if not _excluded(node) and
              any(kind.rsplit('/', 1)[-1] in ('Movie', 'TVSeries') for kind in node.attrs.get('itemtype', '').split())]
    scope = scopes[0] if scopes else doc.root
    if len(scopes) > 1:
        # Multiple unrelated entities are a listing, rather than a detail page.
        return {}
    if item and scopes:
        kind = scope.attrs['itemtype'].split()[0].rsplit('/', 1)[-1]
        if kind in ('Movie', 'TVSeries') and (kind == 'TVSeries') != (item.get('section') == 'series'):
            return {}
        names = [node.text() or node.attrs.get('content', '') for node in doc.nodes
                 if node.prop('name') and node.parent is scope]
        expected = {_normalize(item.get('title')), _normalize(item.get('originalTitle'))} - {''}
        if names and not any(_normalize(name) in expected for name in names):
            return {}
    nodes = [node for node in doc.nodes if not _excluded(node) and
             (scope is doc.root or any(parent is scope for parent in node.ancestors()))]
    result = {}
    for node in nodes:
        person_scope = any('Person' in {kind.rsplit('/', 1)[-1] for kind in parent.attrs.get('itemtype', '').split()}
                           for parent in node.ancestors())
        if node.prop('description') and node.tag != 'meta' and 'description' not in result and not person_scope:
            description = node.text()
            if description and description not in PLACEHOLDERS:
                result['description'] = description
        if node.prop('alternativeHeadline') and 'originalTitle' not in result:
            original = _text(node.attrs.get('content') if node.tag == 'meta' else node.text(), 300)
            if original:
                result['originalTitle'] = original
        for cls, field in [('entity-rating-kp', 'kinopoisk'), ('entity-rating-imdb', 'imdb')]:
            if cls in node.attrs.get('class', '').split() and field not in result:
                score = _score(node.text())
                if score:
                    result[field] = score
    for label, prefix in [('genre', 'genre'), ('country', 'country')]:
        names = _unique(node.text() for node in nodes if label == 'genre' and node.prop('genre'))
        keys = []
        for node in nodes:
            if node.tag != 'a':
                continue
            href = node.attrs.get('href', '')
            try:
                parsed = urlsplit(urljoin(BASE, href))
                valid = (parsed.scheme == 'https' and parsed.hostname == 'w6.zona.plus' and
                         parsed.port in (None, 443) and not parsed.username and not parsed.password and
                         not parsed.query and not parsed.fragment and '%' not in href and '\\' not in href)
            except ValueError:
                valid = False
            match = re.fullmatch(r'/(?:movies|tvseries)/filter/' + prefix + r'-([a-z0-9-]+)', parsed.path) if valid else None
            key = _key(match[1], label == 'country') if match else None
            if key:
                keys.append(key)
                if node.text():
                    names.append(node.text())
        if names:
            result[label] = ', '.join(_unique(names))
        if keys:
            result[label + 'Keys'] = _unique(keys)
            if label == 'country':
                result['countryKeysComplete'] = True
    people = []
    for node in nodes:
        if node.tag != 'a':
            continue
        page_url = catalog_url(node.attrs.get('href'), '/persons/', '/person/', '/people/')
        role = _role(node)
        if page_url and role:
            name_nodes = [child for child in node.descendants() if child.prop('name')]
            name = (name_nodes[0].text() if name_nodes else node.text()).strip(' ,')
            if 0 < len(name) <= 150:
                people.append({'name': name, 'role': role, 'pageUrl': page_url})
    for node in nodes:
        roles = [ROLES[prop] for prop in node.attrs.get('itemprop', '').split() if prop in ROLES]
        if not roles:
            continue
        descendants = list(node.descendants())
        if any(child.tag == 'a' and catalog_url(child.attrs.get('href'), '/persons/', '/person/', '/people/') for child in descendants):
            continue
        names = [child.text() for child in descendants if child.prop('name')]
        name = (names[0] if names else node.text()).strip(' ,')
        if 0 < len(name) <= 150:
            people.append({'name': name, 'role': roles[0], 'pageUrl': ''})
    if people:
        result['people'] = _people(people)
    collections = []
    for node in nodes:
        if node.tag != 'a' or not any(parent.prop('isPartOf') for parent in node.ancestors()):
            continue
        page_url = catalog_url(node.attrs.get('href'), '/collections/', '/franchise/')
        name = node.text()
        if page_url and 0 < len(name) <= 200 and page_url not in {row['pageUrl'] for row in collections}:
            collections.append({'name': name, 'kind': 'Франшиза' if '/franchise/' in page_url else 'Подборка', 'pageUrl': page_url})
        if len(collections) == 30:
            break
    if collections:
        result['collections'] = collections
    return result


def parse_text_metadata(data, item):
    """Return useful, nonempty metadata for a trusted movies/series catalog card."""
    if not isinstance(data, bytes) or not isinstance(item, dict) or not 0 < len(data) <= MAX_BYTES:
        return {}
    section = item.get('section')
    if section not in ('movies', 'series'):
        return {}
    prefix = '/tvseries/' if section == 'series' else '/movies/'
    if not catalog_url(item.get('pageUrl'), prefix):
        return {}
    try:
        text = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return {}
    if text.lstrip().startswith(('{', '[')):
        try:
            root = json.loads(text)
        except (ValueError, RecursionError):
            return {}
        return _json_metadata(root, item) if isinstance(root, dict) else {}
    if '<' not in text:
        return {}
    return parse_html_metadata(text, item)
